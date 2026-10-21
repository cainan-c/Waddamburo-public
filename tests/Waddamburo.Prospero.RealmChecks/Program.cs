global using Xunit;
var checks = new Waddamburo.Architecture.Tests.OsuLazerRealmReaderTests();
checks.ExistingRealmIsReadWithoutChangingItsFiles();
checks.MissingRealmIsNotCreated();
Console.WriteLine("PASS NativeAOT Realm reads an osu!lazer-shaped fixture without changing its files; missing databases stay absent.");
