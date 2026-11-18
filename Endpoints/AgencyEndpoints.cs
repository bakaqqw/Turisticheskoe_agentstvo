using TravelAgency.Data;
using TravelAgency.Models;
using TravelAgency.Services;

namespace TravelAgency.Endpoints;

public static class AgencyEndpoints
{
    public static void MapAgencyEndpoints(this WebApplication app)
    {
        var api=app.MapGroup("/api").RequireAuthorization();
        api.MapGet("/dashboard",(AgencyDatabase db)=>new {
            clientCount=db.Clients().Count,tourCount=db.Tours(activeOnly:true).Count,
            requestCount=db.Bookings().Count,latest=db.Bookings().Take(7),today=AppClock.Today.ToString("yyyy-MM-dd")
        });
        api.MapGet("/lookups",(AgencyDatabase db)=>db.Lookups());
        api.MapGet("/clients",(string? q,AgencyDatabase db)=>db.Clients(q));
        api.MapGet("/clients/{id:long}",(long id,AgencyDatabase db)=>
            db.Clients(id:id).SingleOrDefault() is { } client ? Results.Ok(new {client,bookings=db.Bookings(clientId:id)}) : Results.NotFound());
        api.MapPost("/clients",(ClientInput input,AgencyDatabase db)=>SaveClient(input,null,db));
        api.MapPut("/clients/{id:long}",(long id,ClientInput input,AgencyDatabase db)=>SaveClient(input,id,db));
        api.MapGet("/tours",(bool? activeOnly,AgencyDatabase db)=>db.Tours(activeOnly:activeOnly??false));
        api.MapGet("/tours/{id:long}",(long id,AgencyDatabase db)=>
            db.Tours(id:id).SingleOrDefault() is { } tour ? Results.Ok(tour) : Results.NotFound());
        api.MapPost("/tours",(TourInput input,AgencyDatabase db)=>SaveTour(input,null,db)).RequireAuthorization(p=>p.RequireRole("admin"));
        api.MapPut("/tours/{id:long}",(long id,TourInput input,AgencyDatabase db)=>SaveTour(input,id,db)).RequireAuthorization(p=>p.RequireRole("admin"));
        api.MapGet("/bookings",(AgencyDatabase db)=>db.Bookings());
        api.MapGet("/bookings/{id:long}",(long id,AgencyDatabase db)=>
            db.Bookings(id:id).SingleOrDefault() is { } booking ? Results.Ok(booking) : Results.NotFound());
        api.MapPost("/bookings",CreateBooking);
        // Editing bookings, status changes, cancellation and payments belong to the next stage.
    }
    private static IResult SaveClient(ClientInput input,long? id,AgencyDatabase db)
    {
        var validation=new InputValidation(); var clean=validation.CleanClient(input);
        if (!validation.IsValid) return Results.ValidationProblem(validation.Errors);
        var saved=db.SaveClient(clean,id);
        return saved==0 ? Results.NotFound() : Results.Ok(new {id=saved});
    }
    private static IResult SaveTour(TourInput input,long? id,AgencyDatabase agency)
    {
        var v=new InputValidation(); var title=v.Text(input.Title,"title","Название",200,true);
        var services=v.Text(input.Services,"services","Услуги",4000);
        var starts=v.Date(input.StartsOn,"startsOn");var ends=v.Date(input.EndsOn,"endsOn");
        if (starts!="" && ends!="" && string.CompareOrdinal(ends,starts)<0) v.Error("endsOn","Дата окончания не может быть раньше начала.");
        var price=v.Money(input.Price,"price");
        using var db=agency.Open(); using var tx=db.BeginTransaction();
        if (AgencyDatabase.Scalar(db,"SELECT COUNT(*) FROM hotels WHERE id=$id",tx,("$id",input.HotelId))==0) v.Error("hotelId","Выберите отель из списка.");
        if (AgencyDatabase.Scalar(db,"SELECT COUNT(*) FROM tour_operators WHERE id=$id",tx,("$id",input.OperatorId))==0) v.Error("operatorId","Выберите туроператора.");
        if (id.HasValue) {
            using var command=AgencyDatabase.Command(db,"SELECT hotel_id,operator_id,starts_on,ends_on,services FROM tours WHERE id=$id",tx,("$id",id));
            using var reader=command.ExecuteReader();
            if (!reader.Read()) return Results.NotFound();
            var termsChanged=reader.GetInt64(0)!=input.HotelId || reader.GetInt64(1)!=input.OperatorId || reader.GetString(2)!=starts || reader.GetString(3)!=ends || reader.GetString(4)!=services;
            reader.Close();
            if (termsChanged && AgencyDatabase.Scalar(db,"SELECT COUNT(*) FROM bookings WHERE tour_id=$id",tx,("$id",id))>0)
                v.Error("","Тур уже связан с заявками. Создайте новое предложение для других дат, отеля или услуг.");
        }
        if (!v.IsValid) return Results.ValidationProblem(v.Errors);
        var sql=id.HasValue ? """
            UPDATE tours SET title=$title,hotel_id=$hotel,operator_id=$operator,starts_on=$start,ends_on=$end,
            services=$services,price_kopecks=$price,is_active=$active WHERE id=$id
            """ : """
            INSERT INTO tours(title,hotel_id,operator_id,starts_on,ends_on,services,price_kopecks,is_active)
            VALUES($title,$hotel,$operator,$start,$end,$services,$price,$active)
            """;
        AgencyDatabase.Execute(db,sql,tx,("$title",title),("$hotel",input.HotelId),("$operator",input.OperatorId),
            ("$start",starts),("$end",ends),("$services",services),("$price",price),("$active",input.IsActive),("$id",id));
        var saved=id ?? AgencyDatabase.Scalar(db,"SELECT last_insert_rowid()",tx);tx.Commit();
        return Results.Ok(new {id=saved});
    }
    private static IResult CreateBooking(BookingInput input,AgencyDatabase agency)
    {
        var v=new InputValidation();var total=v.Money(input.TotalPrice,"totalPrice");
        if (input.TravelersCount is <1 or >100) v.Error("travelersCount","Количество туристов: от 1 до 100.");
        using var db=agency.Open(); using var tx=db.BeginTransaction();
        if (AgencyDatabase.Scalar(db,"SELECT COUNT(*) FROM clients WHERE id=$id",tx,("$id",input.ClientId))==0) v.Error("clientId","Выберите клиента.");
        if (AgencyDatabase.Scalar(db,"SELECT COUNT(*) FROM employees WHERE id=$id AND is_active=1",tx,("$id",input.ManagerId))==0) v.Error("managerId","Выберите действующего сотрудника.");
        if (AgencyDatabase.Scalar(db,"SELECT COUNT(*) FROM tours WHERE id=$id AND is_active=1 AND starts_on >= $today",tx,
            ("$id",input.TourId),("$today",AppClock.Today.ToString("yyyy-MM-dd")))==0) v.Error("tourId","Выберите активный тур с предстоящей поездкой.");
        if (!v.IsValid) return Results.ValidationProblem(v.Errors);
        AgencyDatabase.Execute(db,"""
            INSERT INTO bookings(client_id,tour_id,manager_id,travelers_count,total_price_kopecks,status,created_at)
            VALUES($client,$tour,$manager,$count,$price,'new',$now)
            """,tx,("$client",input.ClientId),("$tour",input.TourId),("$manager",input.ManagerId),
            ("$count",input.TravelersCount),("$price",total),("$now",AppClock.Now));
        var id=AgencyDatabase.Scalar(db,"SELECT last_insert_rowid()",tx);
        tx.Commit();return Results.Ok(new {id});
    }
}
