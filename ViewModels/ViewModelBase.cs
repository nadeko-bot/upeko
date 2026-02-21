using CommunityToolkit.Mvvm.ComponentModel;
using upeko.Services;

namespace upeko.ViewModels;

public class ViewModelBase : ObservableObject
{
    public LocalizationService Loc => LocalizationService.Instance;
}
