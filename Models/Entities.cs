namespace TravelAgency.Models;

public sealed record Employee(long Id, string Username, string FullName, string Role, string PasswordHash, bool IsActive);
public sealed record Client(long Id, string LastName, string FirstName, string Patronymic, string Phone, string Email, string Comments);
public sealed record Tour(long Id, string Title, long HotelId, long OperatorId, string Country, string City,
    string Hotel, string Operator, string StartsOn, string EndsOn, string Services, long PriceKopecks, bool IsActive);
public sealed record Booking(long Id, long ClientId, long TourId, long ManagerId, string ClientName, string Phone,
    string TourTitle, string Hotel, string Destination, string StartsOn, string EndsOn, string ManagerName,
    int TravelersCount, long TotalPriceKopecks, string Status, string CreatedAt);

public sealed record LoginInput(string? Username, string? Password);
public sealed record ClientInput(string? LastName, string? FirstName, string? Patronymic, string? Phone, string? Email, string? Comments);
public sealed record TourInput(string? Title, long HotelId, long OperatorId, string? StartsOn, string? EndsOn,
    string? Services, string? Price, bool IsActive);
public sealed record BookingInput(long ClientId, long TourId, long ManagerId, int TravelersCount, string? TotalPrice);
