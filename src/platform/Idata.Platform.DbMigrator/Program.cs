// DbMigrator — i VETMI vend ku aplikohen migrimet e target database-it
// (EF Core = migration owner i vetem; API host-i nuk migron kurre).
// Logjika + guard-et jetojne ne MigratorApp (te testueshme in-process);
// down-to-zero eshte TEST-ONLY dhe fail-closed pa guard-et e plota.
return await Idata.Platform.DbMigrator.MigratorApp.RunAsync(args);
