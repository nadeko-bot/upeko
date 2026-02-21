using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Serilog;

namespace upeko.ViewModels
{
    public abstract partial class DepViewModel : ViewModelBase
    {
        public string Name { get; }

        public IAsyncRelayCommand InstallCommand { get; }

        [ObservableProperty]
        private DepState _state;

        public bool IsChecking => State == DepState.Checking;
        public bool IsNotInstalled => State == DepState.NotInstalled;
        public bool IsInstalled => State == DepState.Installed;

        public string StatusString => State switch
        {
            DepState.Checking => "checking",
            DepState.Installed => "installed",
            DepState.NotInstalled => "missing",
            _ => "checking"
        };

        public DepViewModel(string name)
        {
            Name = name;
            InstallCommand = new AsyncRelayCommand(InstallAsync);
            State = DepState.Checking;
        }

        partial void OnStateChanged(DepState value)
        {
            OnPropertyChanged(nameof(IsChecking));
            OnPropertyChanged(nameof(IsNotInstalled));
            OnPropertyChanged(nameof(IsInstalled));
            OnPropertyChanged(nameof(StatusString));
        }

        public async Task InstallAsync()
        {
            if (State == DepState.NotInstalled)
            {
                Log.Information("Installing dependency: {Name}", Name);
                State = DepState.Checking;

                var success = await InternalInstallAsync();
                State = success ? DepState.Installed : DepState.NotInstalled;
                Log.Information("Dependency {Name} install result: {Status}", Name, StatusString);

                return;
            }

            throw new InvalidOperationException(
                "You can only install the dependency if it is in NotInstalled state.");
        }

        public async Task CheckAsync()
        {
            State = DepState.Checking;
            var newState = await InternalCheckAsync();
            State = newState;
            Log.Information("Dependency {Name}: {Status}", Name, StatusString);
        }

        protected abstract Task<DepState> InternalCheckAsync();
        protected abstract Task<bool> InternalInstallAsync();
    }
}

public enum DepState
{
    Checking,
    NotInstalled,
    Installed,
}