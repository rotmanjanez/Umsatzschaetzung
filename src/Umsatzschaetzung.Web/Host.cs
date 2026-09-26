using Avalonia;
using Avalonia.Browser;
using Avalonia.Controls.ApplicationLifetimes;
using Umsatzschaetzung.App.Ui;
using Umsatzschaetzung.Service;

namespace Umsatzschaetzung.Web;

// The program as one view in the page element with the given id, over whatever services its head composes.
public static class Host
{
    public static async Task<ShellView> Start(Services service, string element)
    {
        App.App.SingleViewService = () => service;
        await AppBuilder.Configure<App.App>().WithInterFont().StartBrowserAppAsync(element);
        return (ShellView)((ISingleViewApplicationLifetime)Application.Current!.ApplicationLifetime!).MainView!;
    }
}
