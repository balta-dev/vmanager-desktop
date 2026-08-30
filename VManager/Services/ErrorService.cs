using System;
using Avalonia.Controls;
using VManager.Views;

namespace VManager.Services
{
    public static class ErrorService
    {
        private static ErrorWindow? _instance;
        private static bool _closed = true;

        public static void Show(
            Exception ex,
            Window? owner = null,
            string? title = null,
            string? color = null)
        {
            Show(ex.Message, owner, title, color);
        }

        public static void Show(
            string message,
            Window? owner = null,
            string? title = null,
            string? color = null)
        {
            if (Avalonia.Application.Current == null || Avalonia.Application.Current.ApplicationLifetime == null)
            {
                Console.WriteLine($"[ErrorService Headless] {title ?? "Error"}: {message}");
                return;
            }

            if (_instance == null || _closed)
            {
                _instance = new ErrorWindow(message, title, color);
                _closed = false;

                _instance.Closed += (_, _) =>
                {
                    _closed = true;
                    _instance = null;
                };

                if (owner != null)
                    _instance.Show(owner);
                else
                    _instance.Show();
            }
            else
            {
                _instance.Activate();
            }
        }
    }
}