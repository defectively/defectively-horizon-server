using System.ComponentModel;
using System.Windows;

namespace Defectively.HorizonServer
{
    /// <summary>
    /// Interaktionslogik für MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        public MainWindow() {
            InitializeComponent();
        }

        private async void OnButtonClick(object sender, RoutedEventArgs e) {
            var wrapper = new ServerWrapper();
            await wrapper.Initialize();
        }

        private void OnClosing(object sender, CancelEventArgs e) {
            e.Cancel = true;
        }
    }
}
