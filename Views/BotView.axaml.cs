using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using upeko.ViewModels;

namespace upeko.Views
{
    public partial class BotView : UserControl
    {
        private BotViewModel? _viewModel;

        public BotView()
        {
            InitializeComponent();
        }

        /// <summary>
        /// Event handler for when the bot name is clicked
        /// </summary>
        /// <param name="sender">The sender object</param>
        /// <param name="e">The event arguments</param>
        private void OnNameTapped(object? sender, RoutedEventArgs e)
        {
            if (DataContext is BotViewModel viewModel)
            {
                viewModel.EditNameCommand.Execute(null);
            }
        }

        protected override void OnDataContextChanged(System.EventArgs e)
        {
            base.OnDataContextChanged(e);

            if (_viewModel is not null)
                _viewModel.PropertyChanged -= OnViewModelPropertyChanged;

            _viewModel = DataContext as BotViewModel;
            if (_viewModel is not null)
                _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        }

        protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnAttachedToVisualTree(e);
            Dispatcher.UIThread.Post(() => LogScroll.ScrollToEnd(), DispatcherPriority.Background);
        }

        protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnDetachedFromVisualTree(e);
            if (_viewModel is not null)
                _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
            _viewModel = null;
        }

        private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName != nameof(BotViewModel.LogText))
                return;

            // Follow new lines only when the user is at the end, so that reading older lines is not interrupted.
            var atEnd = LogScroll.Offset.Y >= LogScroll.Extent.Height - LogScroll.Viewport.Height - 8;
            if (atEnd)
                Dispatcher.UIThread.Post(() => LogScroll.ScrollToEnd(), DispatcherPriority.Background);
        }
    }
}
