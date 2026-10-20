using OrbusGoblinTimer;

int tests = 0;
void Check(bool passes, string name)
{
    tests++;
    if (!passes) throw new Exception("FAILED: " + name);
    Console.WriteLine("PASS: " + name);
}

var anchor = new DateTimeOffset(2026, 10, 20, 12, 0, 0, TimeSpan.Zero);
Check(SpawnClock.NextSpawn(anchor, anchor, 4) == anchor.AddHours(4), "Exact boundary");
Check(SpawnClock.NextSpawn(anchor, anchor.AddMinutes(1), 4) == anchor.AddHours(4), "Minute after");
Check(SpawnClock.NextSpawn(anchor, anchor.AddHours(12), 4) == anchor.AddHours(16), "Multiple cycles");
Check(SpawnClock.NextSpawn(anchor, anchor.AddHours(-1), 4) == anchor, "Before first spawn");
Check(SpawnClock.FormatRemaining(TimeSpan.FromHours(2) + TimeSpan.FromMinutes(3) + TimeSpan.FromSeconds(4)) == "02:03:04", "Countdown format");
Check(SpawnClock.IsSpawnAnnouncement("<b>A Trickster Goblin has spawned in the wilds!</b>"), "HTML announcement");
Check(SpawnClock.IsSpawnAnnouncement("The Trickster Goblin appeared in an overworld zone"), "Plain announcement");
Check(!SpawnClock.IsSpawnAnnouncement("Trickster Goblin will spawn every 4 hours"), "Patch notes not a spawn");
Check(!SpawnClock.IsSpawnAnnouncement("Trickster Goblin was killed in Highsteppe"), "Death is not a spawn");
Check(!SpawnClock.IsSpawnAnnouncement("The next event is a Goblin"), "Unrelated message");
Console.WriteLine($"PASS: {tests} clock/announcement smoke tests.");
