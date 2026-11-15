using System.Windows;
using SerialMonitor.App.ViewModels;

namespace SerialMonitor.App;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    private MainViewModel? ViewModel => DataContext as MainViewModel;

    private void OnSerialModeChecked(object sender, RoutedEventArgs e)
    {
        // IsSerialMode는 읽기 전용이라 선택을 뷰모델로 되돌려준다.
        if (ViewModel is { } vm)
            vm.UseSimulator = false;
    }

    protected override void OnClosed(EventArgs e)
    {
        // 수신 스레드와 CSV 파일을 확실히 닫는다.
        ViewModel?.Dispose();
        base.OnClosed(e);
    }
}
