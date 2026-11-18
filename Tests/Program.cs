using Microsoft.Data.Sqlite;
using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text.Json.Nodes;

// Integration tests launch a separate server and an isolated temporary SQLite database.
var root=Directory.GetCurrentDirectory();
var serverDll=Path.GetFullPath(args.Length>0?args[0]:Path.Combine(root,"bin/Release/net10.0/TravelAgency.dll"));
if (!File.Exists(serverDll)) throw new FileNotFoundException("Сначала выполните dotnet build -c Release из корня проекта.",serverDll);
var temp=Path.Combine(Path.GetTempPath(),"travel-agency-tests-"+Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(temp);
var listener=new TcpListener(IPAddress.Loopback,0);listener.Start();
var port=((IPEndPoint)listener.LocalEndpoint).Port;listener.Stop();
var address=$"http://127.0.0.1:{port}";
Process? server=null;
var logs=new System.Text.StringBuilder();
int passed=0;

async Task StartServer() {
    var info=new ProcessStartInfo(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH")??"dotnet") {
        WorkingDirectory=root,RedirectStandardOutput=true,RedirectStandardError=true,UseShellExecute=false
    };
    info.ArgumentList.Add(serverDll);info.ArgumentList.Add("--urls");info.ArgumentList.Add(address);
    info.Environment["TRAVEL_DATA_DIR"]=temp;
    server=Process.Start(info)!;
    server.OutputDataReceived+=(_,e)=>{if(e.Data is not null)lock(logs)logs.AppendLine(e.Data);};
    server.ErrorDataReceived+=(_,e)=>{if(e.Data is not null)lock(logs)logs.AppendLine(e.Data);};
    server.BeginOutputReadLine();server.BeginErrorReadLine();
    using var probe=new HttpClient();
    for(var i=0;i<100;i++) {
        try {using var response=await probe.GetAsync(address+"/");if(response.IsSuccessStatusCode)return;}
        catch(HttpRequestException) { }
        if(server.HasExited)throw new Exception("Сервер завершился: "+logs);
        await Task.Delay(100);
    }
    throw new Exception("Сервер не запустился: "+logs);
}
void StopServer() {if(server is not null&&!server.HasExited){server.Kill(entireProcessTree:true);server.WaitForExit();}server?.Dispose();}
void Check(bool condition,string message) {if(!condition)throw new Exception(message);}
void CheckSchema() {
    using var db=new SqliteConnection("Data Source="+Path.Combine(temp,"travel_agency.sqlite3"));db.Open();
    using var command=db.CreateCommand();
    command.CommandText="SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name IN ('payments','booking_status_history','app_meta')";
    Check(Convert.ToInt64(command.ExecuteScalar())==0,"Removed tables were recreated");
    command.CommandText="SELECT COUNT(*) FROM sqlite_master WHERE type='table'";
    Check(Convert.ToInt64(command.ExecuteScalar())==9,"Unexpected table count");
    command.CommandText="PRAGMA user_version";
    Check(Convert.ToInt64(command.ExecuteScalar())==4,"Unexpected schema version");
    command.CommandText="SELECT COUNT(*) FROM pragma_table_info('employees') WHERE name='password_new'";
    Check(Convert.ToInt64(command.ExecuteScalar())==1,"Missing password_new");
}
async Task Test(string name,Func<Task> run) {await run();passed++;Console.WriteLine($"OK {passed:00}: {name}");}
async Task<int> ResetPassword(string account, string input) {
    var info = new ProcessStartInfo(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH")??"dotnet") {
        WorkingDirectory=root,RedirectStandardInput=true,RedirectStandardOutput=true,RedirectStandardError=true,UseShellExecute=false
    };
    info.ArgumentList.Add(serverDll);info.ArgumentList.Add("--change-password");info.ArgumentList.Add(account);
    info.Environment["TRAVEL_DATA_DIR"]=temp;
    using var process=Process.Start(info)!;
    var output=process.StandardOutput.ReadToEndAsync();var error=process.StandardError.ReadToEndAsync();
    await process.StandardInput.WriteAsync(input);process.StandardInput.Close();
    await process.WaitForExitAsync();await output;await error;
    return process.ExitCode;
}
string StoredHash(string username) {
    using var db=new SqliteConnection("Data Source="+Path.Combine(temp,"travel_agency.sqlite3"));db.Open();
    using var query=db.CreateCommand();query.CommandText="SELECT password_hash FROM employees WHERE username=$name";
    query.Parameters.AddWithValue("$name",username);return (string)query.ExecuteScalar()!;
}
try {
    await StartServer();
    CheckSchema();
    using var anonymous=new Session(address);using var manager=new Session(address);using var admin=new Session(address);
    long clientId=0,bookingId=0; JsonNode? createdBooking=null;
    await Test("Anonymous data requests return 401",async()=>{
        foreach(var path in new[]{"/api/clients","/api/tours","/api/bookings","/api/lookups","/api/dashboard"})
            Check((await anonymous.Send("GET",path)).Status==401,path);
    });
    await Test("Login requires a CSRF token",async()=>Check((await anonymous.Send("POST","/api/auth/login",new{username="manager",password="TravelManager!2027"},false)).Status==400,"Missing CSRF accepted"));
    await Test("Wrong password is rejected",async()=>{
        await anonymous.Token();Check((await anonymous.Send("POST","/api/auth/login",new{username="manager",password="wrong"})).Status==401,"Wrong password accepted");
    });
    await Test("Manager can sign in and read own role",async()=>{
        await manager.Login("manager","TravelManager!2027");var me=await manager.Send("GET","/api/auth/me");Check(me.Json?["role"]?.GetValue<string>()=="manager","Role");
    });
    await Test("Manager cannot change tours",async()=>Check((await manager.Send("POST","/api/tours",new{})).Status==403,"Role bypass"));
    await Test("Administrator can sign in",async()=>{await admin.Login("admin","TravelAdmin!2027");Check((await admin.Send("GET","/api/auth/me")).Json?["role"]?.GetValue<string>()=="admin","Admin role");});
    await Test("Client form validates required names and phone",async()=>{
        var r=await manager.Send("POST","/api/clients",new{lastName="",firstName="Анна",phone="abc"});
        Check(r.Status==400 && r.Json?["errors"]?["lastName"] is not null && r.Json?["errors"]?["phone"] is not null,"Validation fields");
    });
    await Test("Client is stored with normalized phone",async()=>{
        var r=await manager.Send("POST","/api/clients",new{lastName="Тестов",firstName="Иван",patronymic="",phone="8 (900) 111-22-33",email="test@example.invalid",comments="Комментарий"});
        Check(r.Status==200,"Create client");clientId=r.Json!["id"]!.GetValue<long>();
        Check((await manager.Send("GET","/api/clients/"+clientId)).Json?["client"]?["phone"]?.GetValue<string>()=="+79001112233","Normalized phone");
    });
    await Test("Client changes are persisted",async()=>{
        var r=await manager.Send("PUT","/api/clients/"+clientId,new{lastName="Тестов",firstName="Иван",phone="+79001112233",comments="Изменено"});
        Check(r.Status==200 && (await manager.Send("GET","/api/clients/"+clientId)).Json?["client"]?["comments"]?.GetValue<string>()=="Изменено","Client update");
    });
    await Test("Search supports Russian names and phone",async()=>{
        foreach(var q in new[]{"тестов","9001112233"})Check((await manager.Send("GET","/api/clients?q="+Uri.EscapeDataString(q))).Json!.AsArray().Count==1,"Search "+q);
        Check((await manager.Send("GET","/api/clients?q="+Uri.EscapeDataString("' OR 1=1--"))).Json!.AsArray().Count==0,"Search injection");
    });
    await Test("Inactive tours cannot be booked",async()=>{
        Check(!(await manager.Send("GET","/api/tours?activeOnly=true")).Json!.AsArray().Any(t=>t?["id"]?.GetValue<long>()==4),"Inactive in list");
        Check((await manager.Send("POST","/api/bookings",new{clientId,tourId=4,managerId=2,travelersCount=1,totalPrice="100000"})).Status==400,"Inactive booked");
    });
    await Test("Administrator can create and deactivate a tour",async()=>{
        var tour=new{title="Тестовый тур",hotelId=1,operatorId=1,startsOn="2027-09-01",endsOn="2027-09-08",services="Проживание",price="120000.50",isActive=true};
        var r=await admin.Send("POST","/api/tours",tour);Check(r.Status==200,"Create tour");var id=r.Json!["id"]!.GetValue<long>();
        var changed=await admin.Send("PUT","/api/tours/"+id,new{tour.title,tour.hotelId,tour.operatorId,tour.startsOn,tour.endsOn,tour.services,tour.price,isActive=false});
        Check(changed.Status==200 && (await admin.Send("GET","/api/tours/"+id)).Json?["isActive"]?.GetValue<bool>()==false,"Deactivate tour");
    });
    await Test("Booking uses integer kopecks and initial status only",async()=>{
        var r=await manager.Send("POST","/api/bookings",new{clientId,tourId=1,managerId=2,travelersCount=2,totalPrice="180000.01",status="completed"});
        Check(r.Status==200,"Create booking");bookingId=r.Json!["id"]!.GetValue<long>();
        createdBooking=(await manager.Send("GET","/api/bookings/"+bookingId)).Json;
        Check(createdBooking?["status"]?.GetValue<string>()=="new"&&createdBooking?["totalPriceKopecks"]?.GetValue<long>()==18000001,"Booking fields");
    });
    await Test("Client history includes the new booking",async()=>Check((await manager.Send("GET","/api/clients/"+clientId)).Json!["bookings"]!.AsArray().Any(b=>b?["id"]?.GetValue<long>()==bookingId),"Client history"));
    await Test("Unfinished operations have no working API",async()=>{
        foreach(var action in new[]{"","/status","/payments","/cancel"}){
            var r=await admin.Send(action==""?"PUT":"POST","/api/bookings/"+bookingId+action,new{status="cancelled",amount="100"});Check(r.Status==404,"Unexpected operation "+action);
        }
        Check((await admin.Send("GET","/api/payments")).Status==404,"Payments API");
        Check((await manager.Send("GET","/api/bookings/"+bookingId)).Json?["status"]?.GetValue<string>()=="new","Booking mutated");
    });
    await Test("Invalid money and missing references are rejected",async()=>{
        Check((await manager.Send("POST","/api/bookings",new{clientId,tourId=1,managerId=2,travelersCount=1,totalPrice="10.001"})).Status==400,"Fraction of kopeck");
        Check((await manager.Send("POST","/api/bookings",new{clientId=999999,tourId=1,managerId=2,travelersCount=1,totalPrice="100"})).Status==400,"Missing client accepted");
    });
    await Test("Authenticated writes require CSRF",async()=>Check((await manager.Send("POST","/api/clients",new{lastName="Иванов",firstName="Иван",phone="79000000000"},false)).Status==400,"Missing CSRF accepted"));
    await Test("HTML, CSS and JavaScript are served",async()=>{
        using var http=new HttpClient();foreach(var path in new[]{"/","/css/app.css","/js/app.js"})Check((await http.GetAsync(address+path)).IsSuccessStatusCode,"Static "+path);
        Check((await http.GetStringAsync(address+"/")).Contains("Информационная система для туристического агентства"),"Title");
    });
    await Test("Logout closes the authenticated session",async()=>{
        Check((await manager.Send("POST","/api/auth/logout")).Status==204,"Logout");Check((await manager.Send("GET","/api/clients")).Status==401,"Session remains");
    });
    await Test("SQLite data survives restart and metadata-free upgrade",async()=>{
        StopServer();
        using (var manualDb=new SqliteConnection("Data Source="+Path.Combine(temp,"travel_agency.sqlite3"))) {
            manualDb.Open();using var marker=manualDb.CreateCommand();marker.CommandText="PRAGMA user_version=0";marker.ExecuteNonQuery();
        }
        await StartServer();CheckSchema();using var restored=new Session(address);await restored.Login("admin","TravelAdmin!2027");
        Check((await restored.Send("GET","/api/bookings/"+bookingId)).Json?["totalPriceKopecks"]?.GetValue<long>()==18000001,"Data not persistent");
    });
    await Test("Password utility rejects invalid input, preserves data and closes old sessions",async()=>{
        foreach (var account in new[]{"admin","manager"}) {
            var oldPassword=account=="admin"?"TravelAdmin!2027":"TravelManager!2027";
            var newPassword="ChangedForTest!2027_"+account;
            using var oldSession=new Session(address);await oldSession.Login(account,oldPassword);
            var oldHash=StoredHash(account);
            Check(await ResetPassword(account,"short\nshort\n")==1,"Short password accepted");
            Check(StoredHash(account)==oldHash,"Invalid input changed hash");
            Check((await oldSession.Send("GET","/api/auth/me")).Status==200,"Invalid reset closed session");
            Check(await ResetPassword(account,"Mismatch!2027\nOther!2027\n")==1,"Mismatched password accepted");
            Check(StoredHash(account)==oldHash,"Mismatch changed hash");
            Check(await ResetPassword(account,newPassword+"\n"+newPassword+"\n")==0,"Password reset failed");
            Check(StoredHash(account)!=oldHash && StoredHash(account)!=newPassword,"Password not hashed");
            Check((await oldSession.Send("GET","/api/auth/me")).Status==401,"Old session survived reset");
            using var fresh=new Session(address);await fresh.Token();
            Check((await fresh.Send("POST","/api/auth/login",new{username=account,password=oldPassword})).Status==401,"Old password accepted");
            await fresh.Login(account,newPassword);
            Check((await fresh.Send("GET","/api/auth/me")).Json?["role"]?.GetValue<string>()==account,"Role changed");
            Check((await fresh.Send("GET","/api/bookings/"+bookingId)).Json?["totalPriceKopecks"]?.GetValue<long>()==18000001,"Booking changed");
            Check((await fresh.Send("GET","/api/clients/"+clientId)).Json?["client"]?["comments"]?.GetValue<string>()=="Изменено","Client changed");
        }
        Check(await ResetPassword("unknown","")==1,"Unknown user accepted");
        CheckSchema();
    });
    await Test("Version 3 database gains password_new without changing records",async()=>{
        var hash=StoredHash("admin");StopServer();
        using(var db=new SqliteConnection("Data Source="+Path.Combine(temp,"travel_agency.sqlite3"))) {
            db.Open();using var command=db.CreateCommand();
            command.CommandText="ALTER TABLE employees DROP COLUMN password_new; PRAGMA user_version=3;";command.ExecuteNonQuery();
        }
        await StartServer();CheckSchema();Check(StoredHash("admin")==hash,"Upgrade changed password");
        using var user=new Session(address);await user.Login("admin","ChangedForTest!2027_admin");
        Check((await user.Send("GET","/api/bookings/"+bookingId)).Json?["totalPriceKopecks"]?.GetValue<long>()==18000001,"Upgrade changed booking");
    });
    await Test("Database-entered passwords are hashed and cleared at startup",async()=>{
        using var oldSession=new Session(address);await oldSession.Login("admin","ChangedForTest!2027_admin");
        var hashes=new[]{StoredHash("admin"),StoredHash("manager")};StopServer();
        using(var db=new SqliteConnection("Data Source="+Path.Combine(temp,"travel_agency.sqlite3"))) {
            db.Open();using var command=db.CreateCommand();
            command.CommandText="UPDATE employees SET password_new=$password WHERE username=$username";
            foreach(var username in new[]{"admin","manager"}) {
                command.Parameters.Clear();command.Parameters.AddWithValue("$username",username);
                command.Parameters.AddWithValue("$password","DatabasePassword!2027_"+username);command.ExecuteNonQuery();
            }
        }
        await StartServer();CheckSchema();
        Check((await oldSession.Send("GET","/api/auth/me")).Status==401,"Old session survived database reset");
        foreach(var username in new[]{"admin","manager"}) {
            var hash=StoredHash(username);Check(hash!=hashes[username=="admin"?0:1] && hash!="DatabasePassword!2027_"+username,"Hash not changed");
            using var fresh=new Session(address);await fresh.Token();
            Check((await fresh.Send("POST","/api/auth/login",new{username,password="ChangedForTest!2027_"+username})).Status==401,"Old password accepted");
            await fresh.Login(username,"DatabasePassword!2027_"+username);
            Check((await fresh.Send("GET","/api/auth/me")).Json?["role"]?.GetValue<string>()==username,"Role changed");
            Check((await fresh.Send("GET","/api/bookings/"+bookingId)).Json?["totalPriceKopecks"]?.GetValue<long>()==18000001,"Booking changed");
        }
        using(var db=new SqliteConnection("Data Source="+Path.Combine(temp,"travel_agency.sqlite3"))) {
            db.Open();using var command=db.CreateCommand();
            command.CommandText="SELECT COUNT(*) FROM employees WHERE password_new<>''";
            Check(Convert.ToInt64(command.ExecuteScalar())==0,"Plaintext not cleared");
        }
        var finalHash=StoredHash("admin");StopServer();await StartServer();
        Check(StoredHash("admin")==finalHash,"Password hashed twice");
        StopServer();
        using(var db=new SqliteConnection("Data Source="+Path.Combine(temp,"travel_agency.sqlite3"))) {
            db.Open();using var command=db.CreateCommand();
            command.CommandText="UPDATE employees SET password_new='short' WHERE username='admin'";command.ExecuteNonQuery();
        }
        await StartServer();Check(StoredHash("admin")==finalHash,"Invalid pending password changed hash");
        using var stillValid=new Session(address);await stillValid.Login("admin","DatabasePassword!2027_admin");
    });
    Console.WriteLine($"PASS: {passed}/23 integration tests.");
} finally {StopServer();try{Directory.Delete(temp,true);}catch(IOException){} }

sealed class Session : IDisposable {
    private readonly HttpClient http;private string csrf="";
    public Session(string address) {http=new HttpClient(new HttpClientHandler{CookieContainer=new CookieContainer()}){BaseAddress=new Uri(address)};}
    public async Task Token(){csrf=(await Send("GET","/api/auth/csrf")).Json!["token"]!.GetValue<string>();}
    public async Task Login(string username,string password){await Token();var r=await Send("POST","/api/auth/login",new{username,password});if(r.Status!=200)throw new Exception("Login failed: "+r.Json);await Token();}
    public async Task<(int Status,JsonNode? Json)> Send(string method,string path,object? body=null,bool token=true){
        using var request=new HttpRequestMessage(new HttpMethod(method),path);
        if(body is not null)request.Content=JsonContent.Create(body);
        if(token && method!="GET")request.Headers.Add("X-CSRF-TOKEN",csrf);
        using var response=await http.SendAsync(request);var text=await response.Content.ReadAsStringAsync();
        return ((int)response.StatusCode,text.Length>0?JsonNode.Parse(text):null);
    }
    public void Dispose()=>http.Dispose();
}
