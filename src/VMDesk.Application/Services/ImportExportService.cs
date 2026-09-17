using System.Text.Json;
using VMDesk.Core.Entities;
using VMDesk.Core.Enums;
using VMDesk.Core.Interfaces;
using VMDesk.Core.Models;

namespace VMDesk.Application.Services;

/// <summary>
/// Export/import of VM metadata (spec §25). Exported files never contain passwords;
/// CredentialReference is intentionally stripped. Imports are validated and offer
/// Merge / CreateNew / Skip conflict handling.
/// </summary>
public sealed class ImportExportService : IImportExportService
{
    private readonly IVmRepository _repository;
    private readonly IAppLog _log;

    internal static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public ImportExportService(IVmRepository repository, IAppLogFactory logFactory)
    {
        _repository = repository;
        _log = logFactory.GetLogger("ImportExport");
    }

    public async Task ExportAsync(string filePath, IReadOnlyList<VirtualMachineEntity> vms)
    {
        var groupNames = vms
            .Where(v => v.Group is not null)
            .Select(v => v.Group!.Name)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var file = new ImportExportFile
        {
            AppVersion = AppVersion.Value,
            ExportedAt = DateTimeOffset.UtcNow,
            Groups = groupNames,
            VirtualMachines = vms.Select(ImportMapper.ToExportVm).ToList()
        };

        var json = JsonSerializer.Serialize(file, JsonOptions);
        await File.WriteAllTextAsync(filePath, json);
        _log.Info($"Exported {file.VirtualMachines.Count} VMs to '{Path.GetFileName(filePath)}'.");
    }

    public async Task<ImportExportFile> ReadAsync(string filePath)
    {
        var json = await File.ReadAllTextAsync(filePath);
        var file = JsonSerializer.Deserialize<ImportExportFile>(json, JsonOptions)
            ?? throw new InvalidDataException("The file is not a valid VMDesk export.");

        if (file.SchemaVersion > ImportExportFile.CurrentVersion)
        {
            throw new InvalidDataException($"The export schema version {file.SchemaVersion} is newer than this application supports.");
        }

        Validate(file);
        return file;
    }

    public async Task<ImportResult> ImportAsync(string filePath, ImportConflictStrategy strategy)
    {
        var file = await ReadAsync(filePath);
        var existing = (await _repository.GetAllAsync()).ToList();
        var existingById = existing.ToDictionary(v => v.Id);
        var warnings = new List<string>();
        var imported = 0;
        var merged = 0;
        var skipped = 0;

        foreach (var export in file.VirtualMachines)
        {
            if (existingById.TryGetValue(export.Id, out var current))
            {
                switch (strategy)
                {
                    case ImportConflictStrategy.Merge:
                        ImportMapper.ApplyExport(current, export);
                        await _repository.UpdateAsync(current);
                        merged++;
                        break;
                    case ImportConflictStrategy.CreateNew:
                        var copy = new VirtualMachineEntity();
                        ImportMapper.ApplyExport(copy, export);
                        copy.Id = Guid.NewGuid();
                        copy.Name = UniqueName(copy.Name, existing);
                        copy.CredentialReference = VmFilter.CredentialReferenceFor(copy.Id);
                        await _repository.AddAsync(copy);
                        existing.Add(copy);
                        existingById[copy.Id] = copy;
                        imported++;
                        break;
                    default:
                        skipped++;
                        break;
                }
            }
            else
            {
                var vm = new VirtualMachineEntity();
                ImportMapper.ApplyExport(vm, export);
                if (strategy == ImportConflictStrategy.CreateNew)
                {
                    vm.Id = Guid.NewGuid();
                    vm.Name = UniqueName(vm.Name, existing);
                }

                vm.CredentialReference = VmFilter.CredentialReferenceFor(vm.Id);
                await _repository.AddAsync(vm);
                existing.Add(vm);
                existingById[vm.Id] = vm;
                imported++;
            }
        }

        _log.Info($"Import from '{Path.GetFileName(filePath)}': {imported} imported, {merged} merged, {skipped} skipped.");
        return new ImportResult(imported, merged, skipped, warnings);
    }

    private static string UniqueName(string baseName, List<VirtualMachineEntity> existing)
    {
        var name = baseName;
        var n = 2;
        while (existing.Any(v => string.Equals(v.Name, name, StringComparison.OrdinalIgnoreCase)))
        {
            name = $"{baseName} ({n++})";
        }

        return name;
    }

    private static void Validate(ImportExportFile file)
    {
        var problems = new List<string>();
        var seen = new HashSet<Guid>();

        foreach (var vm in file.VirtualMachines)
        {
            if (vm.Id == Guid.Empty)
            {
                problems.Add("A VM entry has an empty Id.");
            }
            else if (!seen.Add(vm.Id))
            {
                problems.Add($"Duplicate VM Id in file: {vm.Id}.");
            }

            if (string.IsNullOrWhiteSpace(vm.Name))
            {
                problems.Add("A VM entry has an empty Name.");
            }

            if (string.IsNullOrWhiteSpace(vm.Host))
            {
                problems.Add($"VM '{vm.Name}' has an empty Host.");
            }
        }

        if (problems.Count > 0)
        {
            throw new InvalidDataException("Import file validation failed: " + string.Join(" ", problems));
        }
    }
}
