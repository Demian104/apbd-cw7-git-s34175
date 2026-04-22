using System.Data;
using ClinicAdoNet.DTOs;
using Microsoft.Data.SqlClient;

namespace ClinicAdoNet.Services;

public class AppointmentService
{
    private readonly string _connectionString;

    public AppointmentService(IConfiguration configuration)
    {
        _connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("Connection string not found.");
    }

    public async Task<List<AppointmentListDto>> GetAppointmentsAsync(string? status, string? patientLastName)
    {
        var result = new List<AppointmentListDto>();

        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync();

        await using var command = new SqlCommand("""
            SELECT
                a.IdAppointment,
                a.AppointmentDate,
                a.Status,
                a.Reason,
                p.FirstName + N' ' + p.LastName AS PatientFullName,
                p.Email AS PatientEmail
            FROM dbo.Appointments a
            JOIN dbo.Patients p ON p.IdPatient = a.IdPatient
            WHERE (@Status IS NULL OR a.Status = @Status)
              AND (@PatientLastName IS NULL OR p.LastName = @PatientLastName)
            ORDER BY a.AppointmentDate;
            """, connection);

        command.Parameters.Add("@Status", SqlDbType.NVarChar, 30).Value =
            string.IsNullOrEmpty(status) ? DBNull.Value : status;
        command.Parameters.Add("@PatientLastName", SqlDbType.NVarChar, 80).Value =
            string.IsNullOrEmpty(patientLastName) ? DBNull.Value : patientLastName;

        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            result.Add(new AppointmentListDto
            {
                IdAppointment = reader.GetInt32(reader.GetOrdinal("IdAppointment")),
                AppointmentDate = reader.GetDateTime(reader.GetOrdinal("AppointmentDate")),
                Status = reader.GetString(reader.GetOrdinal("Status")),
                Reason = reader.GetString(reader.GetOrdinal("Reason")),
                PatientFullName = reader.GetString(reader.GetOrdinal("PatientFullName")),
                PatientEmail = reader.GetString(reader.GetOrdinal("PatientEmail")),
            });
        }

        return result;
    }

    public async Task<AppointmentDetailsDto?> GetAppointmentByIdAsync(int idAppointment)
    {
        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync();

        await using var command = new SqlCommand("""
            SELECT
                a.IdAppointment,
                a.AppointmentDate,
                a.Status,
                a.Reason,
                a.InternalNotes,
                a.CreatedAt,
                p.IdPatient,
                p.FirstName AS PatientFirstName,
                p.LastName  AS PatientLastName,
                p.Email     AS PatientEmail,
                p.PhoneNumber,
                p.DateOfBirth,
                d.IdDoctor,
                d.FirstName AS DoctorFirstName,
                d.LastName  AS DoctorLastName,
                d.LicenseNumber,
                s.Name      AS Specialization
            FROM dbo.Appointments a
            JOIN dbo.Patients        p ON p.IdPatient       = a.IdPatient
            JOIN dbo.Doctors         d ON d.IdDoctor        = a.IdDoctor
            JOIN dbo.Specializations s ON s.IdSpecialization = d.IdSpecialization
            WHERE a.IdAppointment = @IdAppointment;
            """, connection);

        command.Parameters.Add("@IdAppointment", SqlDbType.Int).Value = idAppointment;

        await using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync()) return null;

        return new AppointmentDetailsDto
        {
            IdAppointment  = reader.GetInt32(reader.GetOrdinal("IdAppointment")),
            AppointmentDate = reader.GetDateTime(reader.GetOrdinal("AppointmentDate")),
            Status         = reader.GetString(reader.GetOrdinal("Status")),
            Reason         = reader.GetString(reader.GetOrdinal("Reason")),
            InternalNotes  = reader.IsDBNull(reader.GetOrdinal("InternalNotes"))
                                 ? null
                                 : reader.GetString(reader.GetOrdinal("InternalNotes")),
            CreatedAt      = reader.GetDateTime(reader.GetOrdinal("CreatedAt")),
            Patient = new PatientInfoDto
            {
                IdPatient   = reader.GetInt32(reader.GetOrdinal("IdPatient")),
                FirstName   = reader.GetString(reader.GetOrdinal("PatientFirstName")),
                LastName    = reader.GetString(reader.GetOrdinal("PatientLastName")),
                Email       = reader.GetString(reader.GetOrdinal("PatientEmail")),
                PhoneNumber = reader.GetString(reader.GetOrdinal("PhoneNumber")),
                DateOfBirth = DateOnly.FromDateTime(reader.GetDateTime(reader.GetOrdinal("DateOfBirth"))),
            },
            Doctor = new DoctorInfoDto
            {
                IdDoctor       = reader.GetInt32(reader.GetOrdinal("IdDoctor")),
                FirstName      = reader.GetString(reader.GetOrdinal("DoctorFirstName")),
                LastName       = reader.GetString(reader.GetOrdinal("DoctorLastName")),
                LicenseNumber  = reader.GetString(reader.GetOrdinal("LicenseNumber")),
                Specialization = reader.GetString(reader.GetOrdinal("Specialization")),
            },
        };
    }

    public async Task<(int? NewId, string? Error, int StatusCode)> CreateAppointmentAsync(
        CreateAppointmentRequestDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Reason))
            return (null, "Reason cannot be empty.", 400);
        if (dto.Reason.Length > 250)
            return (null, "Reason must be at most 250 characters.", 400);
        if (dto.AppointmentDate <= DateTime.UtcNow)
            return (null, "Appointment date cannot be in the past.", 400);

        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync();

        await using var patientCmd = new SqlCommand(
            "SELECT IsActive FROM dbo.Patients WHERE IdPatient = @IdPatient;", connection);
        patientCmd.Parameters.Add("@IdPatient", SqlDbType.Int).Value = dto.IdPatient;
        var patientResult = await patientCmd.ExecuteScalarAsync();
        if (patientResult is null) return (null, "Patient not found.", 400);
        if (!(bool)patientResult)  return (null, "Patient is not active.", 400);

        await using var doctorCmd = new SqlCommand(
            "SELECT IsActive FROM dbo.Doctors WHERE IdDoctor = @IdDoctor;", connection);
        doctorCmd.Parameters.Add("@IdDoctor", SqlDbType.Int).Value = dto.IdDoctor;
        var doctorResult = await doctorCmd.ExecuteScalarAsync();
        if (doctorResult is null) return (null, "Doctor not found.", 400);
        if (!(bool)doctorResult)  return (null, "Doctor is not active.", 400);

        await using var conflictCmd = new SqlCommand("""
            SELECT COUNT(1) FROM dbo.Appointments
            WHERE IdDoctor = @IdDoctor
              AND AppointmentDate = @AppointmentDate
              AND Status = N'Scheduled';
            """, connection);
        conflictCmd.Parameters.Add("@IdDoctor", SqlDbType.Int).Value = dto.IdDoctor;
        conflictCmd.Parameters.Add("@AppointmentDate", SqlDbType.DateTime2).Value = dto.AppointmentDate;
        if ((int)(await conflictCmd.ExecuteScalarAsync())! > 0)
            return (null, "Doctor already has an appointment at that time.", 409);

        await using var insertCmd = new SqlCommand("""
            INSERT INTO dbo.Appointments (IdPatient, IdDoctor, AppointmentDate, Status, Reason, InternalNotes)
            OUTPUT INSERTED.IdAppointment
            VALUES (@IdPatient, @IdDoctor, @AppointmentDate, N'Scheduled', @Reason, NULL);
            """, connection);
        insertCmd.Parameters.Add("@IdPatient", SqlDbType.Int).Value = dto.IdPatient;
        insertCmd.Parameters.Add("@IdDoctor", SqlDbType.Int).Value = dto.IdDoctor;
        insertCmd.Parameters.Add("@AppointmentDate", SqlDbType.DateTime2).Value = dto.AppointmentDate;
        insertCmd.Parameters.Add("@Reason", SqlDbType.NVarChar, 250).Value = dto.Reason;

        var newId = (int)(await insertCmd.ExecuteScalarAsync())!;
        return (newId, null, 201);
    }

    public async Task<(bool Found, string? Error, int StatusCode)> UpdateAppointmentAsync(
        int idAppointment, UpdateAppointmentRequestDto dto)
    {
        var validStatuses = new[] { "Scheduled", "Completed", "Cancelled" };
        if (!validStatuses.Contains(dto.Status))
            return (true, $"Status must be one of: {string.Join(", ", validStatuses)}.", 400);
        if (string.IsNullOrWhiteSpace(dto.Reason))
            return (true, "Reason cannot be empty.", 400);
        if (dto.Reason.Length > 250)
            return (true, "Reason must be at most 250 characters.", 400);

        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync();

        await using var existCmd = new SqlCommand(
            "SELECT Status, AppointmentDate FROM dbo.Appointments WHERE IdAppointment = @IdAppointment;",
            connection);
        existCmd.Parameters.Add("@IdAppointment", SqlDbType.Int).Value = idAppointment;
        await using var existReader = await existCmd.ExecuteReaderAsync();
        if (!await existReader.ReadAsync()) return (false, null, 404);

        var currentStatus = existReader.GetString(existReader.GetOrdinal("Status"));
        var currentDate   = existReader.GetDateTime(existReader.GetOrdinal("AppointmentDate"));
        await existReader.CloseAsync();

        if (currentStatus == "Completed" && dto.AppointmentDate != currentDate)
            return (true, "Cannot change the date of a completed appointment.", 400);

        await using var patientCmd = new SqlCommand(
            "SELECT IsActive FROM dbo.Patients WHERE IdPatient = @IdPatient;", connection);
        patientCmd.Parameters.Add("@IdPatient", SqlDbType.Int).Value = dto.IdPatient;
        var patientResult = await patientCmd.ExecuteScalarAsync();
        if (patientResult is null) return (true, "Patient not found.", 400);
        if (!(bool)patientResult)  return (true, "Patient is not active.", 400);

        await using var doctorCmd = new SqlCommand(
            "SELECT IsActive FROM dbo.Doctors WHERE IdDoctor = @IdDoctor;", connection);
        doctorCmd.Parameters.Add("@IdDoctor", SqlDbType.Int).Value = dto.IdDoctor;
        var doctorResult = await doctorCmd.ExecuteScalarAsync();
        if (doctorResult is null) return (true, "Doctor not found.", 400);
        if (!(bool)doctorResult)  return (true, "Doctor is not active.", 400);

        await using var conflictCmd = new SqlCommand("""
            SELECT COUNT(1) FROM dbo.Appointments
            WHERE IdDoctor = @IdDoctor
              AND AppointmentDate = @AppointmentDate
              AND Status = N'Scheduled'
              AND IdAppointment <> @IdAppointment;
            """, connection);
        conflictCmd.Parameters.Add("@IdDoctor", SqlDbType.Int).Value = dto.IdDoctor;
        conflictCmd.Parameters.Add("@AppointmentDate", SqlDbType.DateTime2).Value = dto.AppointmentDate;
        conflictCmd.Parameters.Add("@IdAppointment", SqlDbType.Int).Value = idAppointment;
        if ((int)(await conflictCmd.ExecuteScalarAsync())! > 0)
            return (true, "Doctor already has an appointment at that time.", 409);

        await using var updateCmd = new SqlCommand("""
            UPDATE dbo.Appointments
            SET IdPatient     = @IdPatient,
                IdDoctor      = @IdDoctor,
                AppointmentDate = @AppointmentDate,
                Status        = @Status,
                Reason        = @Reason,
                InternalNotes = @InternalNotes
            WHERE IdAppointment = @IdAppointment;
            """, connection);
        updateCmd.Parameters.Add("@IdPatient", SqlDbType.Int).Value = dto.IdPatient;
        updateCmd.Parameters.Add("@IdDoctor", SqlDbType.Int).Value = dto.IdDoctor;
        updateCmd.Parameters.Add("@AppointmentDate", SqlDbType.DateTime2).Value = dto.AppointmentDate;
        updateCmd.Parameters.Add("@Status", SqlDbType.NVarChar, 30).Value = dto.Status;
        updateCmd.Parameters.Add("@Reason", SqlDbType.NVarChar, 250).Value = dto.Reason;
        updateCmd.Parameters.Add("@InternalNotes", SqlDbType.NVarChar, 500).Value =
            (object?)dto.InternalNotes ?? DBNull.Value;
        updateCmd.Parameters.Add("@IdAppointment", SqlDbType.Int).Value = idAppointment;
        await updateCmd.ExecuteNonQueryAsync();

        return (true, null, 200);
    }

    public async Task<(bool Found, string? Error, int StatusCode)> DeleteAppointmentAsync(int idAppointment)
    {
        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync();

        await using var existCmd = new SqlCommand(
            "SELECT Status FROM dbo.Appointments WHERE IdAppointment = @IdAppointment;", connection);
        existCmd.Parameters.Add("@IdAppointment", SqlDbType.Int).Value = idAppointment;
        var status = await existCmd.ExecuteScalarAsync() as string;

        if (status is null)       return (false, null, 404);
        if (status == "Completed") return (true, "Cannot delete a completed appointment.", 409);

        await using var deleteCmd = new SqlCommand(
            "DELETE FROM dbo.Appointments WHERE IdAppointment = @IdAppointment;", connection);
        deleteCmd.Parameters.Add("@IdAppointment", SqlDbType.Int).Value = idAppointment;
        await deleteCmd.ExecuteNonQueryAsync();

        return (true, null, 204);
    }
}