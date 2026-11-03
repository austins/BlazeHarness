using BlazeHarness.Core.Sandbox.Ports;

namespace BlazeHarness.Core.Tests.Unit.Sandbox.Ports;

public sealed class ListeningPortsTests
{
    private const string Header = "  sl  local_address rem_address   st tx_queue rx_queue tr tm->when retrnsmt   uid  timeout inode\n";

    [Fact]
    public void Parse_Ipv4Table_KeepsListeningLoopbackAndAnyAddressPorts()
    {
        // Arrange
        var table = Header +
            "   0: 0100007F:223E 00000000:0000 0A 00000000:00000000 00:00000000 00000000  1000        0 1 1 0 100 0 0 10 0\n" +
            "   1: 00000000:0BB8 00000000:0000 0A 00000000:00000000 00:00000000 00000000  1000        0 2 1 0 100 0 0 10 0\n" +
            "   2: 0100007F:1F90 0100007F:D431 01 00000000:00000000 00:00000000 00000000  1000        0 3 1 0 100 0 0 10 0\n" +
            "   3: 6400020A:1F91 00000000:0000 0A 00000000:00000000 00:00000000 00000000  1000        0 4 1 0 100 0 0 10 0\n";

        // Act
        var ports = ListeningPorts.Parse(table, ipv6: false).ToList();

        // Assert
        ports.Should().Equal(new ListeningPort(8766, true), new ListeningPort(3000, true));
    }

    [Fact]
    public void Parse_Ipv6Table_TellsLoopbackOnlyFromAnyAddress()
    {
        // Arrange
        var table = Header +
            "   0: 00000000000000000000000001000000:1435 00000000000000000000000000000000:0000 0A 00000000:00000000 00:00000000 00000000  1000        0 1 1 0 100 0 0 10 0\n" +
            "   1: 00000000000000000000000000000000:0050 00000000000000000000000000000000:0000 0A 00000000:00000000 00:00000000 00000000  1000        0 2 1 0 100 0 0 10 0\n" +
            "   2: 0000000000000000FFFF00000100007F:0051 00000000000000000000000000000000:0000 0A 00000000:00000000 00:00000000 00000000  1000        0 3 1 0 100 0 0 10 0\n";

        // Act
        var ports = ListeningPorts.Parse(table, ipv6: true).ToList();

        // Assert
        ports.Should().Equal(new ListeningPort(5173, false), new ListeningPort(80, true), new ListeningPort(81, true));
    }

    [Fact]
    public void Merge_SamePortOverBothFamilies_IsReachedOverIpv4()
    {
        // Act
        var ports = ListeningPorts.Merge([new ListeningPort(5173, false), new ListeningPort(5173, true), new ListeningPort(3000, false)]);

        // Assert
        ports.Should().BeEquivalentTo([new ListeningPort(3000, false), new ListeningPort(5173, true)]);
    }
}
