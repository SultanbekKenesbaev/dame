using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using DailyGate.Shared;
using DailyGate.Windows.Client.Services;
using DailyGate.Windows.Client.ViewModels;

namespace DailyGate.Windows.Client;

public partial class MainWindow : Window
{
    private readonly PipeClient _pipe = new();
    private readonly ObservableCollection<QuestionViewModel> _questions = [];
    private readonly DispatcherTimer _poll = new() { Interval = TimeSpan.FromSeconds(15) };
    private readonly DispatcherTimer _countdown = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly List<ShieldWindow> _shields = [];
    private WarningWindow? _warning;
    private ClientStatus? _status;
    private DailyTestPayload? _payload;
    private DateTimeOffset _startedAt;
    private DateTimeOffset _deadline;
    private bool _submitting;

    public MainWindow()
    {
        InitializeComponent();
        QuestionsList.ItemsSource = _questions;
        _poll.Tick += async (_, _) => await RefreshStatusAsync(silent: true);
        _countdown.Tick += async (_, _) => await TickCountdownAsync();
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        ShowShields();
        _poll.Start();
        await RefreshStatusAsync(silent: false);
    }

    private async Task RefreshStatusAsync(bool silent)
    {
        try
        {
            _status = await _pipe.StatusAsync();
            WorkdayText.Text = $"Рабочий день · {_status.Workday:dd.MM.yyyy}";
            ConnectionText.Text = _status.ConnectionState switch { "online" => "Сервер подключён", "offline" => "Офлайн-режим", "error" => "Ошибка синхронизации", _ => "Подключение проверяется" };
            ConnectionDot.Fill = new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(_status.ConnectionState == "online" ? "#24B485" : "#E8A63B"));
            if (_status.Warning is not null && _status.Unlocked)
            {
                _warning ??= new WarningWindow();
                _warning.UpdateMessage(_status.Warning);
            }
            else if (_warning?.IsVisible == true) _warning.Hide();

            if (_status.Unlocked) UnlockDesktop();
            else if (!_status.Enrolled) ShowBlockingError(_status.Warning ?? "Устройство не зарегистрировано администратором.");
            else if (!_status.EmployeeActive) ShowBlockingError(_status.Warning ?? "Учётная запись сотрудника заблокирована.");
            else if (!IsVisible) { Show(); Activate(); ShowShields(); }
        }
        catch (Exception exception)
        {
            if (!silent) ShowBlockingError($"Не удалось подключиться к системной службе DailyGate.\n\n{exception.Message}");
        }
    }

    private async void LoginButton_Click(object sender, RoutedEventArgs e)
    {
        LoginButton.IsEnabled = false; LoginError.Visibility = Visibility.Collapsed;
        try
        {
            var result = await _pipe.SendAsync<ClientLoginCommand, ClientLoginResult>(PipeOperations.Login,
                new ClientLoginCommand(LoginBox.Text, PasswordBox.Password));
            if (result.MustChangePassword)
            {
                CurrentPasswordBox.Password = PasswordBox.Password;
                PasswordBox.Clear();
                LoginPanel.Visibility = Visibility.Collapsed;
                PasswordChangePanel.Visibility = Visibility.Visible;
            }
            else
            {
                PasswordBox.Clear(); EmployeeName.Text = result.FullName; _startedAt = result.StartedAt;
                LoadTest(result.Test);
            }
        }
        catch (Exception exception) { LoginError.Text = exception.Message; LoginError.Visibility = Visibility.Visible; }
        finally { LoginButton.IsEnabled = true; }
    }

    private void LoadTest(SignedDailyTest signed)
    {
        _payload = signed.Payload(); _questions.Clear();
        foreach (var question in _payload.Questions) _questions.Add(new QuestionViewModel(question));
        TestTitle.Text = _payload.Title; _deadline = _startedAt.AddMinutes(_payload.TimeLimitMinutes);
        LoginPanel.Visibility = Visibility.Collapsed; RecoveryPanel.Visibility = Visibility.Collapsed; PasswordChangePanel.Visibility = Visibility.Collapsed; BlockingError.Visibility = Visibility.Collapsed; TestPanel.Visibility = Visibility.Visible;
        _countdown.Start(); _ = TickCountdownAsync();
    }

    private async Task TickCountdownAsync()
    {
        var remaining = _deadline - DateTimeOffset.UtcNow;
        if (remaining <= TimeSpan.Zero) { TimerText.Text = "00:00"; _countdown.Stop(); if (!_submitting) await SubmitAsync(SubmissionKind.TimedOut); return; }
        TimerText.Text = $"{(int)remaining.TotalMinutes:00}:{remaining.Seconds:00}";
    }

    private async void SubmitButton_Click(object sender, RoutedEventArgs e)
    {
        if (_questions.Any(question => question.Options.All(option => !option.IsSelected))) { TestError.Text = "Ответьте на все вопросы перед отправкой."; return; }
        await SubmitAsync(SubmissionKind.Completed);
    }

    private async Task SubmitAsync(SubmissionKind status)
    {
        if (_submitting) return; _submitting = true; SubmitButton.IsEnabled = false; TestError.Text = "Отправляем ответы…";
        try
        {
            var answers = _questions.Select(question => new SubmissionAnswerRequest(question.Id,
                question.Options.Where(option => option.IsSelected).Select(option => option.Id).ToArray())).ToArray();
            await _pipe.SendAsync<ClientSubmitCommand, ClientSubmitResult>(PipeOperations.Submit,
                new ClientSubmitCommand(status, _startedAt, answers));
            UnlockDesktop();
        }
        catch (Exception exception) { TestError.Text = exception.Message; SubmitButton.IsEnabled = true; _submitting = false; }
    }

    private async void RecoveryButton_Click(object sender, RoutedEventArgs e)
    {
        RecoveryError.Text = "";
        try
        {
            await _pipe.SendAsync<ClientEmergencyCommand, ClientSubmitResult>(PipeOperations.EmergencyUnlock, new ClientEmergencyCommand(RecoveryCodeBox.Text));
            UnlockDesktop();
        }
        catch (Exception exception) { RecoveryError.Text = exception.Message; }
    }

    private async void ChangePasswordButton_Click(object sender, RoutedEventArgs e)
    {
        PasswordChangeError.Text = "";
        if (NewPasswordBox.Password != ConfirmPasswordBox.Password)
        {
            PasswordChangeError.Text = "Новые пароли не совпадают.";
            return;
        }
        try
        {
            var result = await _pipe.SendAsync<ClientPasswordChangeCommand, ClientLoginResult>(PipeOperations.ChangePassword,
                new ClientPasswordChangeCommand(CurrentPasswordBox.Password, NewPasswordBox.Password));
            CurrentPasswordBox.Clear(); NewPasswordBox.Clear(); ConfirmPasswordBox.Clear();
            EmployeeName.Text = result.FullName; _startedAt = result.StartedAt; LoadTest(result.Test);
        }
        catch (Exception exception) { PasswordChangeError.Text = exception.Message; }
    }

    private void ShowRecovery_Click(object sender, RoutedEventArgs e) { LoginPanel.Visibility = Visibility.Collapsed; RecoveryPanel.Visibility = Visibility.Visible; }
    private void BackToLogin_Click(object sender, RoutedEventArgs e) { RecoveryPanel.Visibility = Visibility.Collapsed; LoginPanel.Visibility = Visibility.Visible; }
    private async void RetryButton_Click(object sender, RoutedEventArgs e) => await RefreshStatusAsync(silent: false);

    private void ShowBlockingError(string message)
    {
        LoginPanel.Visibility = Visibility.Collapsed; TestPanel.Visibility = Visibility.Collapsed; RecoveryPanel.Visibility = Visibility.Collapsed; PasswordChangePanel.Visibility = Visibility.Collapsed;
        BlockingErrorText.Text = message; BlockingError.Visibility = Visibility.Visible;
    }

    private void UnlockDesktop()
    {
        _countdown.Stop(); HideShields();
        if (Process.GetProcessesByName("explorer").Length == 0)
            Process.Start(new ProcessStartInfo("explorer.exe") { UseShellExecute = true });
        Hide();
    }

    private void ShowShields()
    {
        if (_shields.Count > 0) return;
        var primary = System.Windows.Forms.Screen.PrimaryScreen;
        foreach (var screen in System.Windows.Forms.Screen.AllScreens.Where(screen => screen != primary))
        {
            var shield = new ShieldWindow(screen); _shields.Add(shield); shield.Show();
        }
    }

    private void HideShields() { foreach (var shield in _shields) shield.Close(); _shields.Clear(); }

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        e.Cancel = true; if (!IsVisible) Show(); Activate();
    }
}
