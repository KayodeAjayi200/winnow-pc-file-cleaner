using System.Management;

namespace FileTinder.Services;

public enum DriverStatus { Recent, CheckRecommended, Outdated, VeryOld, Unknown }

public record DriverInfo(
    string DeviceName,
    string Manufacturer,
    string Version,
    DateTime? DriverDate,
    string DeviceClass,
    string Category,
    DriverStatus Status,
    string? ManufacturerUrl
);

public static class DriverService
{
    // Device classes we care about (skip printers, virtual, etc.)
    private static readonly Dictionary<string, string> ClassToCategory = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Display"]         = "Display & GPU",
        ["Net"]             = "Network",
        ["MEDIA"]           = "Audio & Video",
        ["AudioEndpoint"]   = "Audio & Video",
        ["System"]          = "System & Chipset",
        ["Mouse"]           = "Input Devices",
        ["Keyboard"]        = "Input Devices",
        ["HIDClass"]        = "Input Devices",
        ["USB"]             = "USB Controllers",
        ["DiskDrive"]       = "Storage",
        ["HDC"]             = "Storage",
        ["SCSIAdapter"]     = "Storage",
        ["MTD"]             = "Storage",
        ["Bluetooth"]       = "Bluetooth",
        ["Camera"]          = "Camera",
        ["Processor"]       = "System & Chipset",
        ["Battery"]         = "System & Chipset",
    };

    public static async Task<List<DriverInfo>> ScanAsync() =>
        await Task.Run(Scan);

    public static List<DriverInfo> Scan()
    {
        var results = new List<DriverInfo>();

        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT DeviceName, Manufacturer, DriverVersion, DriverDate, DeviceClass " +
                "FROM Win32_PnPSignedDriver " +
                "WHERE DeviceName IS NOT NULL AND DriverVersion IS NOT NULL");

            foreach (ManagementObject obj in searcher.Get())
            {
                var deviceClass = obj["DeviceClass"]?.ToString() ?? "";
                if (!ClassToCategory.TryGetValue(deviceClass, out var category))
                    continue;

                var name         = obj["DeviceName"]?.ToString()    ?? "Unknown Device";
                var manufacturer = obj["Manufacturer"]?.ToString()  ?? "";
                var version      = obj["DriverVersion"]?.ToString() ?? "—";
                var dateStr      = obj["DriverDate"]?.ToString()    ?? "";

                DateTime? driverDate = ParseWmiDate(dateStr);
                var status           = GetStatus(driverDate);
                var url              = GetManufacturerUrl(manufacturer, name);

                results.Add(new DriverInfo(name, manufacturer, version, driverDate, deviceClass, category, status, url));
            }
        }
        catch { /* WMI unavailable — return empty */ }

        return results
            .OrderBy(d => d.Category)
            .ThenBy(d => d.DeviceName)
            .ToList();
    }

    private static DateTime? ParseWmiDate(string wmiDate)
    {
        // WMI format: "yyyyMMddHHmmss.ffffff+UUU" or "yyyyMMddHHmmss.ffffff-UUU"
        if (string.IsNullOrWhiteSpace(wmiDate) || wmiDate.Length < 8)
            return null;
        try
        {
            var datePart = wmiDate[..8]; // yyyyMMdd
            if (DateTime.TryParseExact(datePart, "yyyyMMdd",
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None, out var dt))
                return dt;
        }
        catch { }
        return null;
    }

    public static DriverStatus GetStatus(DateTime? date)
    {
        if (date is null) return DriverStatus.Unknown;
        var age = DateTime.Now - date.Value;
        return age.TotalDays switch
        {
            < 180  => DriverStatus.Recent,
            < 365  => DriverStatus.CheckRecommended,
            < 730  => DriverStatus.Outdated,
            _      => DriverStatus.VeryOld,
        };
    }

    public static string FormatAge(DateTime? date)
    {
        if (date is null) return "Unknown";
        var age = DateTime.Now - date.Value;
        if (age.TotalDays < 30)  return $"{(int)age.TotalDays}d ago";
        if (age.TotalDays < 365) return $"{(int)(age.TotalDays / 30)}mo ago";
        var years = (int)(age.TotalDays / 365);
        return $"{years}yr{(years > 1 ? "s" : "")} ago";
    }

    private static string? GetManufacturerUrl(string manufacturer, string deviceName)
    {
        var m = manufacturer.ToUpperInvariant();
        var n = deviceName.ToUpperInvariant();

        if (m.Contains("NVIDIA") || n.Contains("NVIDIA") || n.Contains("GEFORCE") || n.Contains("QUADRO"))
            return "https://www.nvidia.com/Download/index.aspx";
        if (m.Contains("AMD") || m.Contains("ADVANCED MICRO DEVICES") || n.Contains("RADEON") || n.Contains("RYZEN"))
            return "https://www.amd.com/en/support/download/drivers.html";
        if (m.Contains("INTEL") || n.Contains("INTEL"))
            return "https://www.intel.com/content/www/us/en/download-center/home.html";
        if (m.Contains("REALTEK") || n.Contains("REALTEK"))
            return "https://www.realtek.com/en/downloads";
        if (m.Contains("QUALCOMM") || n.Contains("QUALCOMM"))
            return "https://www.qualcomm.com/support";
        if (m.Contains("BROADCOM") || n.Contains("BROADCOM"))
            return "https://www.broadcom.com/support/download-search";
        if (m.Contains("MICROSOFT") || n.Contains("MICROSOFT"))
            return null; // Microsoft drivers update via Windows Update, no separate page needed

        // Generic search fallback
        var query = Uri.EscapeDataString($"{manufacturer} {deviceName} driver download");
        return $"https://www.google.com/search?q={query}";
    }
}
