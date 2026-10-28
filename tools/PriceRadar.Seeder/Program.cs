// The seed and stage-fixture commands are implemented on DAY-2 (spec §11.3).
Console.WriteLine("""
    PriceRadar.Seeder

    Commands (DAY-2):
      seed           --books 20000 --listings 60000 --snapshots 500000 --days 180 --seed 42
      stage-fixture  --set run-001
    """);
return args.Length == 0 ? 0 : 1;
