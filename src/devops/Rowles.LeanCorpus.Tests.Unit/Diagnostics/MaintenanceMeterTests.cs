using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using Rowles.LeanCorpus.Document;
using Rowles.LeanCorpus.Index.Backup;
using Rowles.LeanCorpus.Index.Format;
using Rowles.LeanCorpus.Index.Migration;
using Rowles.LeanCorpus.Store;

namespace Rowles.LeanCorpus.Tests.Unit.Diagnostics;

public sealed class MaintenanceMeterTests : IDisposable
{
    private readonly string _root;

    public MaintenanceMeterTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "maintenance_meter_tests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    [Fact(DisplayName = "Maintenance Operations: Emit Meter Instruments")]
    public void MaintenanceOperations_EmitMeterInstruments()
    {
        var measurements = new ConcurrentBag<string>();
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, meterListener) =>
        {
            if (instrument.Meter.Name == "Rowles.LeanCorpus" &&
                instrument.Name.StartsWith("leancorpus.index.", StringComparison.Ordinal))
            {
                meterListener.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<long>((instrument, _, _, _) => measurements.Add(instrument.Name));
        listener.SetMeasurementEventCallback<double>((instrument, _, _, _) => measurements.Add(instrument.Name));
        listener.Start();

        var indexPath = CreateIndex("meter-index");
        var backupPath = Path.Combine(_root, "meter-backup");
        var restorePath = Path.Combine(_root, "meter-restore");

        _ = IndexFormatInspector.Inspect(new MMapDirectory(indexPath));
        _ = IndexCodecMigrator.Plan(new MMapDirectory(indexPath));
        _ = IndexCodecMigrator.Migrate(new MMapDirectory(indexPath), new IndexCodecMigrationOptions { DryRun = true });
        _ = IndexBackup.CreateManifest(indexPath);
        _ = IndexBackup.Backup(indexPath, backupPath);
        _ = IndexBackup.ValidateBackup(backupPath);
        _ = IndexBackup.Restore(backupPath, restorePath);

        Assert.Contains("leancorpus.index.format.inspect.count", measurements);
        Assert.Contains("leancorpus.index.format.inspect.duration", measurements);
        Assert.Contains("leancorpus.index.codec_migration.plan.count", measurements);
        Assert.Contains("leancorpus.index.codec_migration.plan.duration", measurements);
        Assert.Contains("leancorpus.index.codec_migration.migrate.count", measurements);
        Assert.Contains("leancorpus.index.codec_migration.migrate.duration", measurements);
        Assert.Contains("leancorpus.index.backup.manifest.count", measurements);
        Assert.Contains("leancorpus.index.backup.manifest.duration", measurements);
        Assert.Contains("leancorpus.index.backup.copy.count", measurements);
        Assert.Contains("leancorpus.index.backup.copy.duration", measurements);
        Assert.Contains("leancorpus.index.backup.validate.count", measurements);
        Assert.Contains("leancorpus.index.backup.validate.duration", measurements);
        Assert.Contains("leancorpus.index.backup.restore.count", measurements);
        Assert.Contains("leancorpus.index.backup.restore.duration", measurements);
    }

    private string CreateIndex(string name)
    {
        var indexPath = Path.Combine(_root, name);
        Directory.CreateDirectory(indexPath);
        using var writer = new IndexWriter(new MMapDirectory(indexPath), new IndexWriterConfig());
        var document = new LeanDocument();
        document.Add(new TextField("body", "maintenance telemetry meter test"));
        writer.AddDocument(document);
        writer.Commit();
        return indexPath;
    }
}
