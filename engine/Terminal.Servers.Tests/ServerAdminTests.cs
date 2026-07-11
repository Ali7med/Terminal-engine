using Terminal.Servers.Parsing;
using Terminal.Servers.Scan;

namespace Terminal.Servers.Tests;

public class ServerAdminTests
{
    [Fact]
    public void ParseServices_ReadsUnits_SkipsBullet()
    {
        string output =
            "ssh.service loaded active running OpenSSH server daemon\n" +
            "● nginx.service loaded failed failed A high performance web server\n";

        var svc = OutputParsers.ParseServices(output);

        Assert.Equal(2, svc.Count);
        Assert.Equal("ssh.service", svc[0].Name);
        Assert.True(svc[0].IsActive);
        Assert.Equal("OpenSSH server daemon", svc[0].Description);
        Assert.Equal("nginx.service", svc[1].Name);   // تخطّى النقطة ●
        Assert.False(svc[1].IsActive);
    }

    [Fact]
    public void ParsePorts_ExtractsPortAndProcess()
    {
        string output =
            "LISTEN 0 128 0.0.0.0:22 0.0.0.0:* users:((\"sshd\",pid=1234,fd=3))\n" +
            "LISTEN 0 511 127.0.0.1:3306 0.0.0.0:* users:((\"mysqld\",pid=987,fd=21))\n";

        var ports = OutputParsers.ParsePorts(output);

        Assert.Equal(2, ports.Count);
        Assert.Equal(22, ports[0].Port);
        Assert.Equal("0.0.0.0", ports[0].Address);
        Assert.Equal("sshd", ports[0].Process);
        Assert.Equal(3306, ports[1].Port);
        Assert.Equal("mysqld", ports[1].Process);
    }

    [Fact]
    public void BuildKill_WithAndWithoutForce()
    {
        Assert.Equal("kill 4242", ServerAdmin.BuildKill(4242, force: false));
        Assert.Equal("kill -9 4242", ServerAdmin.BuildKill(4242, force: true));
    }

    [Fact]
    public void BuildServiceAction_QuotesName_RejectsBadAction()
    {
        Assert.Equal("systemctl restart 'nginx.service'", ServerAdmin.BuildServiceAction("restart", "nginx.service"));
        Assert.Throws<System.ArgumentException>(() => ServerAdmin.BuildServiceAction("rm -rf /", "x"));
    }
}
