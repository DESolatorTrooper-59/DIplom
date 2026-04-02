using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using Tournaments.WPF.Views;

namespace Tournaments.WPF
{
    public partial class App : Application
    {
        private int _isShowingCrashDialog;

        public App()
        {
            DispatcherUnhandledException += OnDispatcherUnhandledException;
            AppDomain.CurrentDomain.UnhandledException += OnCurrentDomainUnhandledException;
            TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
        }

        private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            ShowCrashDialog("Приложение завершает работу из-за необработанной ошибки.", e.Exception);
            e.Handled = true;
            Shutdown(-1);
        }

        private void OnCurrentDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            Exception exception = e.ExceptionObject as Exception ?? new Exception(Convert.ToString(e.ExceptionObject));
            ShowCrashDialog("Приложение завершает работу из-за необработанной ошибки.", exception);
        }

        private void OnUnobservedTaskException(object sender, UnobservedTaskExceptionEventArgs e)
        {
            ShowCrashDialog("Обнаружена необработанная ошибка фоновой задачи.", e.Exception);
            e.SetObserved();
        }

        private void ShowCrashDialog(string summary, Exception exception)
        {
            if (Interlocked.Exchange(ref _isShowingCrashDialog, 1) == 1)
            {
                return;
            }

            Action showAction = () =>
            {
                try
                {
                    CrashDetailsWindow window = new CrashDetailsWindow(summary, BuildExceptionDetails(exception));
                    if (MainWindow != null && MainWindow.IsLoaded && MainWindow.IsVisible)
                    {
                        window.Owner = MainWindow;
                    }

                    window.ShowDialog();
                }
                catch
                {
                    try
                    {
                        MessageBox.Show(
                            summary + Environment.NewLine + Environment.NewLine + BuildExceptionDetails(exception),
                            "Tournaments WPF - ошибка",
                            MessageBoxButton.OK,
                            MessageBoxImage.Error);
                    }
                    catch
                    {
                    }
                }
                finally
                {
                    Interlocked.Exchange(ref _isShowingCrashDialog, 0);
                }
            };

            try
            {
                Dispatcher dispatcher = Dispatcher ?? Current?.Dispatcher;
                if (dispatcher != null && !dispatcher.CheckAccess())
                {
                    dispatcher.Invoke(showAction);
                    return;
                }
            }
            catch
            {
            }

            showAction();
        }

        private static string BuildExceptionDetails(Exception exception)
        {
            StringBuilder builder = new StringBuilder();
            builder.AppendLine("Время: " + DateTime.Now.ToString("dd.MM.yyyy HH:mm:ss"));
            builder.AppendLine();

            if (exception == null)
            {
                builder.AppendLine("Сведения об исключении отсутствуют.");
                return builder.ToString();
            }

            int level = 0;
            Exception current = exception;
            while (current != null)
            {
                if (level > 0)
                {
                    builder.AppendLine();
                    builder.AppendLine(new string('-', 72));
                    builder.AppendLine();
                }

                builder.AppendLine(level == 0 ? "Основное исключение" : "Внутреннее исключение #" + level);
                builder.AppendLine("Тип: " + current.GetType().FullName);
                builder.AppendLine("Сообщение: " + current.Message);
                builder.AppendLine("Источник: " + (string.IsNullOrWhiteSpace(current.Source) ? "(не указан)" : current.Source));
                builder.AppendLine("Метод: " + (current.TargetSite == null ? "(не указан)" : current.TargetSite.ToString()));
                builder.AppendLine();
                builder.AppendLine("StackTrace:");
                builder.AppendLine(string.IsNullOrWhiteSpace(current.StackTrace) ? "(стек-трейс отсутствует)" : current.StackTrace);

                current = current.InnerException;
                level++;
            }

            return builder.ToString();
        }
    }
}
