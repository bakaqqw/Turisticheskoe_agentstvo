using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Text.RegularExpressions;
using TravelAgency.Models;

namespace TravelAgency.Services;

public static class AppClock
{
    public static DateOnly Today => DateOnly.FromDateTime(DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(5)).DateTime);
    public static string Now => DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture);
}

public sealed class InputValidation
{
    public Dictionary<string, string[]> Errors { get; } = new();
    public bool IsValid => Errors.Count == 0;
    public void Error(string field, string text) => Errors[field] = [text];
    public string Text(string? value, string field, string label, int max, bool required = false)
    {
        var text = (value ?? "").Trim();
        if (required && text.Length == 0) Error(field, $"Заполните поле «{label}».");
        else if (text.Length > max) Error(field, $"Поле «{label}»: не более {max} символов.");
        return text;
    }
    public static string Digits(string? value) => new((value ?? "").Where(c => c is >= '0' and <= '9').ToArray());
    public string Phone(string? value)
    {
        var digits = Digits(value);
        if (digits.Length == 11 && digits.StartsWith('8')) digits = "7" + digits[1..];
        if (digits.Length is < 7 or > 15) Error("phone", "Введите телефон: от 7 до 15 цифр с кодом страны.");
        return "+" + digits;
    }
    public long Money(string? value, string field)
    {
        var text = (value ?? "").Trim().Replace(',', '.');
        if (!Regex.IsMatch(text, @"^\d{1,10}(\.\d{1,2})?$") ||
            !decimal.TryParse(text, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var amount) ||
            amount <= 0 || amount > 9999999999.99m)
        {
            Error(field, "Введите положительную сумму в рублях, не более двух знаков после запятой.");
            return 0;
        }
        return checked((long)(amount * 100m));
    }
    public string Date(string? value, string field)
    {
        if (!DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
        { Error(field, "Укажите корректную дату."); return ""; }
        return date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    }
    public Client CleanClient(ClientInput input)
    {
        var last = Text(input.LastName, "lastName", "Фамилия", 100, true);
        var first = Text(input.FirstName, "firstName", "Имя", 100, true);
        var middle = Text(input.Patronymic, "patronymic", "Отчество", 100);
        var email = Text(input.Email, "email", "Email", 254);
        if (email.Length > 0 && !new EmailAddressAttribute().IsValid(email)) Error("email", "Введите корректный адрес электронной почты.");
        return new Client(0, last, first, middle, Phone(input.Phone), email, Text(input.Comments, "comments", "Комментарий", 2000));
    }
}
