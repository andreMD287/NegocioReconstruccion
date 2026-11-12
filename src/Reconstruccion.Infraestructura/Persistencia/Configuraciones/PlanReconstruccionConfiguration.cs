using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using System.Text.Json;
using Reconstruccion.Dominio.Comun;
using Reconstruccion.Dominio.Planes;

namespace Reconstruccion.Infraestructura.Persistencia.Configuraciones;

public sealed class PlanReconstruccionConfiguration
    : IEntityTypeConfiguration<PlanReconstruccion>
{
    public void Configure(EntityTypeBuilder<PlanReconstruccion> builder)
    {
        builder.ToTable("planes_reconstruccion");

        builder.HasKey(p => p.Id);

        builder.Property(p => p.Id)
            .ValueGeneratedNever();

        // RN-RE-18: version controlada por Dominio.
        // No se utiliza xmin ni generacion automatica.
        builder.Property(p => p.Version)
            .IsConcurrencyToken()
            .ValueGeneratedNever();

        // Un plan por desastre.
        builder.HasIndex(p => p.DesastreId)
            .IsUnique();

        builder.Property(p => p.Estado)
            .HasConversion<string>()
            .HasMaxLength(32);

        // Los eventos no son entidades persistentes.
        // La unidad de trabajo los llevara al outbox.
        builder.Ignore(p => p.Eventos);

        // Necesidades pertenecientes al plan.
        builder.OwnsMany(p => p.Necesidades, necesidad =>
        {
            necesidad.ToTable("necesidades");

            necesidad.WithOwner()
                .HasForeignKey("PlanId");

            necesidad.HasKey(n => n.Id);

            necesidad.Property(n => n.Id)
                .ValueGeneratedNever();

            necesidad.Property(n => n.Estado)
                .HasConversion<string>()
                .HasMaxLength(32);

            // Copia local de la evaluacion de origen.
            necesidad.OwnsOne(n => n.Origen, origen =>
            {
                origen.Property(o => o.EvaluacionId)
                    .HasColumnName("evaluacion_id");

                origen.Property(o => o.VersionEvaluacion)
                    .HasColumnName("version_evaluacion");

                origen.Property(o => o.ActivoId)
                    .HasColumnName("activo_id");

                origen.Property(o => o.TipoActivo)
                    .HasColumnName("tipo_activo");

                origen.Property(o => o.NivelDano)
                    .HasColumnName("nivel_dano");

                origen.Property(o => o.FechaValidacion)
                    .HasColumnName("fecha_validacion");

                // Ubicacion es un record inmutable.
                // Se almacena como JSONB para que EF pueda
                // suministrarla al constructor de EvaluacionOrigen.
                origen.Property(o => o.Ubicacion)
                    .HasConversion(
                        new ValueConverter<Ubicacion, string>(
                            valor => JsonSerializer.Serialize(
                                valor, (JsonSerializerOptions?)null),
                            json => JsonSerializer.Deserialize<Ubicacion>(
                                json, (JsonSerializerOptions?)null)!
                        ),
                        new ValueComparer<Ubicacion>(
                            (a, b) => a == b,
                            valor => valor.GetHashCode(),
                            valor => valor
                        )
                    )
                    .HasColumnName("ubicacion")
                    .HasColumnType("jsonb");
            });
        });

        // Acceso directo a la lista privada del Dominio.
        builder.Navigation(p => p.Necesidades)
            .HasField("_necesidades")
            .UsePropertyAccessMode(PropertyAccessMode.Field);

        // Alertas pertenecientes al plan.
        builder.OwnsMany(p => p.Alertas, alerta =>
        {
            alerta.ToTable("alertas_plan");

            alerta.WithOwner()
                .HasForeignKey("PlanId");

            alerta.HasKey(a => a.Id);

            alerta.Property(a => a.Id)
                .ValueGeneratedNever();

            alerta.Property(a => a.Tipo)
                .HasConversion<string>()
                .HasMaxLength(40);

            alerta.Property(a => a.Estado)
                .HasConversion<string>()
                .HasMaxLength(32);

            // Propiedad calculada del Dominio.
            alerta.Ignore(a => a.EstaActiva);
        });

        builder.Navigation(p => p.Alertas)
            .HasField("_alertas")
            .UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}