using System.Reflection;
using Avalonia.Automation.Peers;
using Avalonia.Controls;

namespace Umsatzschaetzung.Tests.Ui;

// Accessible.Install puts its peers into this private field of Avalonia's; an update that renames it fails here, not in the grids and the scan viewer.
public class TablesTests
{
    [Fact]
    public void ControlKeepsItsPeerInTheFieldInstallWritesTo()
    {
        var field = typeof(Control).GetField("_automationPeer", BindingFlags.Instance | BindingFlags.NonPublic);

        Assert.NotNull(field);
        Assert.Equal(typeof(AutomationPeer), field.FieldType);
    }
}
