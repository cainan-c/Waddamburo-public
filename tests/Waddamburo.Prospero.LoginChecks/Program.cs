using System.Net;
using System.Reflection;
using System.Diagnostics;
using Waddamburo.App.Home;
using Waddamburo.Game.Scores;
using Waddamburo.Platform.Sdl;

var directory = Path.Combine(Path.GetTempPath(), "waddamburo-login-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(directory);
int checks = 0;
try
{
    foreach (string mode in new[] { "timeout", "io", "json", "http", "success", "cancel" })
    {
        var handler = new LoginHandler(mode);
        ScoreClient.PlatformHandlerFactory = _ => handler;
        using var flow = new PlayerSetupFlow(AccountBook.Load(directory), new Uri("https://login.test/"), false, _ => null, directory);
        // Start the actual UI operation without driving platform keyboard/native rendering.
        typeof(PlayerSetupFlow).GetMethod("startDeviceLogin", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(flow, [0]);
        await handler.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        if (mode == "cancel") flow.Close();
        var clock = Stopwatch.StartNew();
        SetupColumn column;
        do
        {
            flow.Tick(SdlKeyboardSnapshot.Empty);
            column = flow.Columns()[0];
            if (mode == "success" && column.Code is not null || mode == "cancel" || column.Message?.StartsWith("Connecting") != true) break;
            await Task.Delay(10);
        } while (clock.Elapsed < TimeSpan.FromSeconds(2));
        bool pass = mode switch
        {
            "timeout" => column.Message?.Contains("timed out") == true,
            "io" => column.Message?.Contains("CA root import") == true,
            "json" => column.Message?.Contains("Login failed") == true,
            "http" => column.Message?.Contains("503") == true,
            "success" => column.Code == "SYNTHETIC" && column.QrUrl == "https://login.test/approve?code=SYNTHETIC",
            "cancel" => column.Message?.Contains("timed out") != true && column.Code is null,
            _ => false
        };
        if (!pass) throw new Exception($"Login {mode} failed: {column.Message}");
        checks++;
        Console.WriteLine($"PASS login {mode}: actual setup flow reports failure or code and respects cancellation");
    }
    Console.WriteLine($"{checks} login flow checks passed (synthetic transport, no server login).");
}
finally
{
    ScoreClient.PlatformHandlerFactory = null;
    Directory.Delete(directory, true);
}

sealed class LoginHandler(string mode) : HttpMessageHandler
{
    public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
    {
        Started.TrySetResult();
        await Task.Yield();
        if (mode == "timeout") throw new TaskCanceledException("Synthetic HttpClient timeout.");
        if (mode == "io") throw new IOException("PS5 CA root import failed (synthetic).");
        if (mode == "cancel") { await Task.Delay(Timeout.Infinite, token); throw new InvalidOperationException(); }
        return new HttpResponseMessage(mode == "http" ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.OK)
        {
            Content = new StringContent(mode == "json" ? "invalid JSON" : "{\"device_code\":\"synthetic-only\",\"user_code\":\"SYNTHETIC\",\"expires_in\":60,\"interval\":60,\"verification_url\":\"https://login.test/approve\"}")
        };
    }
}
