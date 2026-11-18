using System.Text;
using TravelAgency.Data;

namespace TravelAgency.Services;

public static class AccountPasswordConsole
{
    public static int Run(string[] args, AgencyDatabase database)
    {
        Console.OutputEncoding = Encoding.UTF8;
        if (args.Length != 2 || args[1] is not ("admin" or "manager" or "both")) {
            Console.WriteLine("Укажите логин: --change-password admin, manager или both.");
            return 1;
        }
        if (!File.Exists(database.FilePath)) {
            Console.WriteLine("База не найдена: " + database.FilePath);
            Console.WriteLine("Сначала запустите сайт через START_WINDOWS.bat из папки своего проекта.");
            return 1;
        }
        try {
            Console.WriteLine("База: " + database.FilePath);
            Console.WriteLine("Пароль: от 8 до 128 символов. Esc — отмена текущей операции.");
            foreach (var username in args[1] == "both" ? new[] {"admin", "manager"} : new[] {args[1]}) {
                if (database.FindEmployee(username:username) is null)
                    throw new ArgumentException($"Сотрудник с логином {username} не найден.");
                while (true) {
                    Console.WriteLine($"\nСмена пароля: {username}");
                    var password = ReadPassword("Новый пароль: ");
                    var confirmation = ReadPassword("Повторите пароль: ");
                    if (password != confirmation) {
                        Console.WriteLine("Пароли не совпали. Повторите ввод.");
                        continue;
                    }
                    try { AccountPasswordService.ChangePassword(database,username,password); }
                    catch (ArgumentException error) { Console.WriteLine(error.Message); continue; }
                    Console.WriteLine($"Пароль {username} изменён. Выполните вход с новым паролем.");
                    break;
                }
            }
            return 0;
        }
        catch (OperationCanceledException) { Console.WriteLine("Операция отменена."); return 1; }
        catch (Exception error) { Console.WriteLine("Ошибка: " + error.Message); return 1; }
    }

    private static string ReadPassword(string prompt)
    {
        Console.Write(prompt);
        if (Console.IsInputRedirected)
            return Console.ReadLine() ?? throw new OperationCanceledException();
        var value = new StringBuilder();
        while (true) {
            var key = Console.ReadKey(intercept:true);
            if (key.Key == ConsoleKey.Escape) { Console.WriteLine(); throw new OperationCanceledException(); }
            if (key.Key == ConsoleKey.Enter) { Console.WriteLine(); return value.ToString(); }
            if (key.Key == ConsoleKey.Backspace) {
                if (value.Length > 0) { value.Length--; Console.Write("\b \b"); }
            }
            else if (!char.IsControl(key.KeyChar) && value.Length < 129) { value.Append(key.KeyChar); Console.Write('*'); }
        }
    }
}
