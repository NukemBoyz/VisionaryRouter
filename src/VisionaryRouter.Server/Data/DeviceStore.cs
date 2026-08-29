using Microsoft.Data.Sqlite;
using VisionaryRouter.Core.Models;

namespace VisionaryRouter.Server.Data;

/// <summary>
/// SQLite-backed persistence for the device list.
///
/// Only configuration is durable here - names, addresses, types. Live state such as
/// online/offline and current source is deliberately not treated as truth on load: it is
/// stored for display continuity, then re-established from the network at startup. A router
/// that trusted its own saved routing state would confidently show stale sources after
/// anything else on the network changed a route.
/// </summary>
public sealed class DeviceStore
{
    private readonly string _connectionString;

    public DeviceStore(string databasePath)
    {
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath
        }.ToString();
    }

    public void Initialize()
    {
        using var connection = Open();

        using var command = connection.CreateCommand();
        command.CommandText =
            """
            CREATE TABLE IF NOT EXISTS Devices (
                Id            TEXT PRIMARY KEY,
                Name          TEXT NOT NULL,
                Type          INTEGER NOT NULL,
                IpAddress     TEXT NOT NULL,
                ControlPort   INTEGER NOT NULL DEFAULT 8000,
                Enabled       INTEGER NOT NULL DEFAULT 1,
                UnitId        TEXT NULL,
                Model         TEXT NULL,
                SerialNumber  TEXT NULL,
                MacAddress    TEXT NULL,
                Firmware      TEXT NULL,
                FirmwareDate  TEXT NULL,
                StreamMode    TEXT NULL,
                CreatedUtc    TEXT NOT NULL,
                UpdatedUtc    TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS Settings (
                Key   TEXT PRIMARY KEY,
                Value TEXT NOT NULL
            );
            """;
        command.ExecuteNonQuery();
    }

    public List<Device> LoadAll()
    {
        using var connection = Open();

        using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT Id, Name, Type, IpAddress, ControlPort, Enabled,
                   UnitId, Model, SerialNumber, MacAddress, Firmware, FirmwareDate, StreamMode,
                   CreatedUtc, UpdatedUtc
            FROM Devices
            ORDER BY Name
            """;

        var devices = new List<Device>();

        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            devices.Add(new Device
            {
                Id = Guid.Parse(reader.GetString(0)),
                Name = reader.GetString(1),
                Type = (DeviceType)reader.GetInt32(2),
                IpAddress = reader.GetString(3),
                ControlPort = reader.GetInt32(4),
                Enabled = reader.GetInt32(5) != 0,
                UnitId = reader.IsDBNull(6) ? null : reader.GetString(6),
                Model = reader.IsDBNull(7) ? null : reader.GetString(7),
                SerialNumber = reader.IsDBNull(8) ? null : reader.GetString(8),
                MacAddress = reader.IsDBNull(9) ? null : reader.GetString(9),
                Firmware = reader.IsDBNull(10) ? null : reader.GetString(10),
                FirmwareDate = reader.IsDBNull(11) ? null : reader.GetString(11),
                StreamMode = reader.IsDBNull(12) ? null : reader.GetString(12),
                CreatedUtc = DateTime.Parse(reader.GetString(13)).ToUniversalTime(),
                UpdatedUtc = DateTime.Parse(reader.GetString(14)).ToUniversalTime(),

                // Left at their defaults on purpose: rediscovered from the network.
                OnlineState = OnlineState.Unknown,
                CurrentEncoderIp = null
            });
        }

        return devices;
    }

    public void Upsert(Device device)
    {
        using var connection = Open();

        using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO Devices
                (Id, Name, Type, IpAddress, ControlPort, Enabled,
                 UnitId, Model, SerialNumber, MacAddress, Firmware, FirmwareDate, StreamMode,
                 CreatedUtc, UpdatedUtc)
            VALUES
                ($id, $name, $type, $ip, $port, $enabled,
                 $unitId, $model, $serial, $mac, $firmware, $firmwareDate, $streamMode,
                 $created, $updated)
            ON CONFLICT(Id) DO UPDATE SET
                Name = $name, Type = $type, IpAddress = $ip, ControlPort = $port, Enabled = $enabled,
                UnitId = $unitId, Model = $model, SerialNumber = $serial, MacAddress = $mac,
                Firmware = $firmware, FirmwareDate = $firmwareDate, StreamMode = $streamMode,
                UpdatedUtc = $updated
            """;

        command.Parameters.AddWithValue("$id", device.Id.ToString());
        command.Parameters.AddWithValue("$name", device.Name);
        command.Parameters.AddWithValue("$type", (int)device.Type);
        command.Parameters.AddWithValue("$ip", device.IpAddress);
        command.Parameters.AddWithValue("$port", device.ControlPort);
        command.Parameters.AddWithValue("$enabled", device.Enabled ? 1 : 0);
        command.Parameters.AddWithValue("$unitId", (object?)device.UnitId ?? DBNull.Value);
        command.Parameters.AddWithValue("$model", (object?)device.Model ?? DBNull.Value);
        command.Parameters.AddWithValue("$serial", (object?)device.SerialNumber ?? DBNull.Value);
        command.Parameters.AddWithValue("$mac", (object?)device.MacAddress ?? DBNull.Value);
        command.Parameters.AddWithValue("$firmware", (object?)device.Firmware ?? DBNull.Value);
        command.Parameters.AddWithValue("$firmwareDate", (object?)device.FirmwareDate ?? DBNull.Value);
        command.Parameters.AddWithValue("$streamMode", (object?)device.StreamMode ?? DBNull.Value);
        command.Parameters.AddWithValue("$created", device.CreatedUtc.ToString("O"));
        command.Parameters.AddWithValue("$updated", DateTime.UtcNow.ToString("O"));

        command.ExecuteNonQuery();
    }

    public void Delete(Guid id)
    {
        using var connection = Open();

        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM Devices WHERE Id = $id";
        command.Parameters.AddWithValue("$id", id.ToString());
        command.ExecuteNonQuery();
    }

    private SqliteConnection Open()
    {
        var connection = new SqliteConnection(_connectionString);
        connection.Open();
        return connection;
    }
}
