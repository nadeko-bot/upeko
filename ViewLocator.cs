using Avalonia.Controls;
using Avalonia.Controls.Templates;
using upeko.ViewModels;
using upeko.Views;

namespace upeko;

public class ViewLocator : IDataTemplate
{
    public Control? Build(object? param)
    {
        if (param is null)
            return null;

        return param switch
        {
            BotListViewModel => new BotListView(),
            BotViewModel => new BotView(),
            _ => new TextBlock { Text = "Not Found: " + param.GetType().FullName }
        };
    }

    public bool Match(object? data)
    {
        return data is ViewModelBase;
    }
}
