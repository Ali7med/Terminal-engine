using Terminal.Servers.Scan;

namespace Terminal.Servers.Tests;

public class SystemInfoScannerTests
{
    [Fact]
    public void Parse_ReadsAllFields()
    {
        string output =
            "web-01\n" +
            "===OS===\n" +
            "Ubuntu 22.04.3 LTS\n" +
            "===KERNEL===\n" +
            "5.15.0-88-generic\n" +
            "===CPU===\n" +
            " Intel(R) Xeon(R) CPU E5-2680 v4\n" +
            "===CORES===\n" +
            "8\n" +
            "===IP===\n" +
            "10.0.0.5\n";

        var info = SystemInfoScanner.Parse(output);

        Assert.Equal("web-01", info.Hostname);
        Assert.Equal("Ubuntu 22.04.3 LTS", info.OsName);
        Assert.Equal("5.15.0-88-generic", info.Kernel);
        Assert.Equal("Intel(R) Xeon(R) CPU E5-2680 v4", info.CpuModel);
        Assert.Equal(8, info.CpuCores);
        Assert.Equal("10.0.0.5", info.Ip);
    }

    [Fact]
    public void Parse_ToleratesMissingSections()
    {
        // نواة/معالج غائبة (أوامر غير مدعومة) → حقول فارغة لا استثناء
        string output = "myhost\n===OS===\n===KERNEL===\n===CPU===\n===CORES===\n===IP===\n";

        var info = SystemInfoScanner.Parse(output);

        Assert.Equal("myhost", info.Hostname);
        Assert.Equal("", info.OsName);
        Assert.Equal(0, info.CpuCores);
    }
}
