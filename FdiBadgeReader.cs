using HidSharp;
using System.IO;

namespace BadgeFlow.Desktop;

public sealed class BadgePacket
{
    public byte[] Data { get; init; } = Array.Empty<byte>();
    public string HexNumber => Data.Length > 9
        ? $"{Data[9]:X2}{Data[8]:X2}{Data[7]:X2}{Data[6]:X2}"
        : "";
    public string DecimalNumber => Data.Length > 9
        ? BitConverter.ToUInt32(Data, 6).ToString("D10")
        : "";
    public byte TypeByte22 => Data.Length > 22 ? Data[22] : (byte)0;
}

public sealed class FdiBadgeReader : IDisposable
{
    private const int VendorId = 0x1072;
    private const int ProductId = 0x0002;

    private CancellationTokenSource? _cts;
    private Task? _task;
    private string _lastRaw = "";
    private DateTime _lastRead = DateTime.MinValue;

    public event Action<BadgePacket>? PacketRead;
    public event Action<string>? StatusChanged;

    public void Start()
    {
        _cts = new CancellationTokenSource();
        _task = Task.Run(() => RunAsync(_cts.Token));
    }

    private async Task RunAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            var device = DeviceList.Local.GetHidDevices(VendorId, ProductId)
                .OrderByDescending(x => x.GetMaxInputReportLength())
                .FirstOrDefault();

            if (device is null)
            {
                StatusChanged?.Invoke("Encodeur non détecté");
                await Delay(1000, token);
                continue;
            }

            try
            {
                if (!device.TryOpen(out var stream) || stream is null)
                {
                    StatusChanged?.Invoke("Encodeur occupé — fermez Visiosoft");
                    await Delay(1200, token);
                    continue;
                }

                using (stream)
                {
                    stream.ReadTimeout = 700;
                    StatusChanged?.Invoke("Encodeur connecté");

                    byte[] buffer = new byte[Math.Max(65, device.GetMaxInputReportLength())];

                    while (!token.IsCancellationRequested)
                    {
                        try
                        {
                            int count = stream.Read(buffer, 0, buffer.Length);
                            if (count <= 9) continue;

                            byte[] packet = buffer.Take(count).ToArray();
                            string raw = Convert.ToHexString(packet.AsSpan(6, 4));
                            if (raw == "00000000") continue;

                            DateTime now = DateTime.Now;
                            bool duplicate = raw == _lastRaw &&
                                             (now - _lastRead).TotalMilliseconds < 1200;
                            _lastRaw = raw;
                            _lastRead = now;

                            if (!duplicate)
                                PacketRead?.Invoke(new BadgePacket { Data = packet });
                        }
                        catch (TimeoutException) { }
                        catch (IOException)
                        {
                            StatusChanged?.Invoke("Encodeur déconnecté");
                            break;
                        }
                    }
                }
            }
            catch (UnauthorizedAccessException)
            {
                StatusChanged?.Invoke("Accès refusé — fermez les logiciels Urmet");
            }
            catch (Exception ex)
            {
                StatusChanged?.Invoke("Erreur encodeur : " + ex.Message);
            }

            await Delay(900, token);
        }
    }

    private static async Task Delay(int ms, CancellationToken token)
    {
        try { await Task.Delay(ms, token); } catch { }
    }

    public void Dispose()
    {
        try
        {
            _cts?.Cancel();
            _task?.Wait(500);
        }
        catch { }
        _cts?.Dispose();
    }
}
