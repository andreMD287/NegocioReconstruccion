using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Reconstruccion.Infraestructura.Persistencia.Registros;

namespace Reconstruccion.Infraestructura.Persistencia.Configuraciones;

public sealed class AuditoriaPersistidaConfiguration
    : IEntityTypeConfiguration<AuditoriaPersistida>
{
    public void Configure(EntityTypeBuilder<AuditoriaPersistida> builder)
    {
        builder.ToTable("auditoria_operaciones");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();

        builder.Property(x => x.Rol)
            .HasConversion<string>()
            .HasMaxLength(40);

        builder.Property(x => x.Resultado)
            .HasConversion<string>()
            .HasMaxLength(20);

        builder.Property(x => x.Accion)
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(x => x.TipoObjeto)
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(x => x.Fecha)
            .HasColumnType("timestamp with time zone");

        builder.HasIndex(x => new { x.DesastreId, x.Fecha });
        builder.HasIndex(x => new { x.ObjetoId, x.Fecha });
        builder.HasIndex(x => x.RequestId);
    }
}

public sealed class SolicitudProcesadaPersistidaConfiguration
    : IEntityTypeConfiguration<SolicitudProcesadaPersistida>
{
    public void Configure(
        EntityTypeBuilder<SolicitudProcesadaPersistida> builder)
    {
        builder.ToTable("solicitudes_procesadas");

        // RequestId impide confirmar dos veces la misma solicitud.
        builder.HasKey(x => x.RequestId);

        builder.Property(x => x.RequestId)
            .ValueGeneratedNever();

        builder.Property(x => x.TipoComando)
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(x => x.HuellaContenido)
            .HasMaxLength(64)
            .IsRequired();

        // Conservar exactamente la respuesta serializada.
        builder.Property(x => x.RespuestaSerializada)
            .HasColumnType("text")
            .IsRequired();
    }
}

public sealed class EventoOutboxPersistidoConfiguration
    : IEntityTypeConfiguration<EventoOutboxPersistido>
{
    public void Configure(
        EntityTypeBuilder<EventoOutboxPersistido> builder)
    {
        builder.ToTable("eventos_outbox");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id)
            .ValueGeneratedNever();

        builder.Property(x => x.Secuencia)
            .UseIdentityByDefaultColumn();

        builder.HasIndex(x => x.Secuencia)
            .IsUnique();

        builder.Property(x => x.OcurridoEn)
            .HasColumnType("timestamp with time zone");

        builder.Property(x => x.PublicadoEn)
            .HasColumnType("timestamp with time zone");

        builder.Property(x => x.TipoEvento)
            .HasMaxLength(300)
            .IsRequired();

        builder.Property(x => x.ContenidoJson)
            .HasColumnType("jsonb")
            .IsRequired();

        builder.Property(x => x.Intentos)
            .HasDefaultValue(0);

        builder.HasIndex(x => new
        {
            x.PublicadoEn,
            x.DesastreId,
            x.VersionAgregado,
            x.Secuencia
        });

        builder.HasIndex(x => new
        {
            x.DesastreId,
            x.VersionAgregado,
            x.Secuencia
        });
    }
}