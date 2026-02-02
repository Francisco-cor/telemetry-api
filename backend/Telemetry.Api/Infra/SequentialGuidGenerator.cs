using System;

namespace Telemetry.Api.Infra;

public static class SequentialGuidGenerator
{
    public static Guid Create()
    {
        // Usar stackalloc para evitar asignaciones en el heap
        Span<byte> guidBytes = stackalloc byte[16];
        Guid.NewGuid().TryWriteBytes(guidBytes);

        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        Span<byte> timestampBytes = stackalloc byte[8];
        System.Buffers.Binary.BinaryPrimitives.WriteInt64BigEndian(timestampBytes, timestamp);

        Span<byte> result = stackalloc byte[16];

        // Copiar 6 bytes del timestamp (suficiente para ~8000 años)
        // El timestamp en BigEndian tiene los bytes significativos al final si es positivo,
        // pero ToUnixTimeMilliseconds sobre 8 bytes BigEndian pone los bytes significativos a la derecha.
        // Queremos los últimos 6 bytes del long (que son los más significativos para el tiempo actual).
        timestampBytes.Slice(2, 6).CopyTo(result.Slice(0, 6));

        // Copiar los otros 10 bytes de aleatoriedad del GUID original
        guidBytes.Slice(6, 10).CopyTo(result.Slice(6, 10));

        return new Guid(result);
    }
}
