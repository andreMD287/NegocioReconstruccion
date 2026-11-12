using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Reconstruccion.Dominio.Comun;
using Reconstruccion.Dominio.Proyectos;

namespace Reconstruccion.Infraestructura.Persistencia.Configuraciones;

public sealed class ProyectoReconstruccionConfiguration
    : IEntityTypeConfiguration<ProyectoReconstruccion>
{
    public void Configure(EntityTypeBuilder<ProyectoReconstruccion> builder)
    {
        builder.ToTable("proyectos_reconstruccion");

        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id).ValueGeneratedNever();

        builder.Property(p => p.Version)
            .IsConcurrencyToken()
            .ValueGeneratedNever();

        builder.Property(p => p.Nombre).IsRequired().HasMaxLength(250);
        builder.Property(p => p.Descripcion).IsRequired();
        builder.Property(p => p.Tipo).HasConversion<string>().HasMaxLength(60);
        builder.Property(p => p.Estado).HasConversion<string>().HasMaxLength(40);
        builder.Property(p => p.Presupuesto).HasPrecision(18, 2);

        builder.HasIndex(p => p.DesastreId);

        // Propiedades calculadas que no deben persistirse.
        builder.Ignore(p => p.Eventos);
        builder.Ignore(p => p.EsTerminal);
        builder.Ignore(p => p.CostoEjecutado);
        builder.Ignore(p => p.AvanceTotal);
        builder.Ignore(p => p.CambioSolicitado);
        builder.Ignore(p => p.ResponsableId);

        // Lista de Guid almacenada como arreglo PostgreSQL.
        builder.Property(p => p.NecesidadIds)
            .HasField("_necesidadIds")
            .UsePropertyAccessMode(PropertyAccessMode.Field)
            .HasColumnName("necesidad_ids")
            .HasColumnType("uuid[]");

        // Participantes opcionales.
        builder.OwnsOne(p => p.Responsable, responsable =>
        {
            responsable.Property(r => r.UsuarioId)
                .HasColumnName("responsable_usuario_id");

            responsable.Property(r => r.OrganismoId)
                .HasColumnName("responsable_organismo_id");
        });

        builder.OwnsOne(p => p.Contratista, contratista =>
        {
            contratista.Property(c => c.Identificacion)
                .HasColumnName("contratista_identificacion");

            contratista.Property(c => c.Nombre)
                .HasColumnName("contratista_nombre");
        });

        // Aportes del presupuesto vigente.
        builder.OwnsMany(p => p.Aportes, aporte =>
        {
            aporte.ToTable("aportes_proyecto");
            aporte.WithOwner().HasForeignKey("ProyectoId");

            aporte.Property<int>("Id").ValueGeneratedOnAdd();
            aporte.HasKey("ProyectoId", "Id");

            aporte.Property(a => a.Monto).HasPrecision(18, 2);
            aporte.Property(a => a.Fuente).IsRequired();
        });

        builder.Navigation(p => p.Aportes)
            .HasField("_aportes")
            .UsePropertyAccessMode(PropertyAccessMode.Field);

        // Hitos del cronograma.
        builder.OwnsMany(p => p.Hitos, hito =>
        {
            hito.ToTable("hitos_proyecto");
            hito.WithOwner().HasForeignKey("ProyectoId");

            hito.HasKey(h => h.Id);
            hito.Property(h => h.Id).ValueGeneratedNever();

            hito.Property(h => h.Peso).HasPrecision(7, 2);
            hito.Property(h => h.Avance).HasPrecision(7, 2);
            hito.Ignore(h => h.EstaCompleto);
        });

        builder.Navigation(p => p.Hitos)
            .HasField("_hitos")
            .UsePropertyAccessMode(PropertyAccessMode.Field);

        // Historial de avances y sus evidencias.
        builder.OwnsMany(p => p.Avances, avance =>
        {
            avance.ToTable("avances_proyecto");
            avance.WithOwner().HasForeignKey("ProyectoId");

            avance.HasKey(a => a.Id);
            avance.Property(a => a.Id).ValueGeneratedNever();

            avance.Property(a => a.Porcentaje).HasPrecision(7, 2);
            avance.Property(a => a.PorcentajeAnterior).HasPrecision(7, 2);

            avance.OwnsMany(a => a.Evidencias, evidencia =>
            {
                evidencia.ToTable("evidencias_avance");
                evidencia.WithOwner().HasForeignKey("AvanceId");

                evidencia.Property<int>("Id").ValueGeneratedOnAdd();
                evidencia.HasKey("AvanceId", "Id");
            });

            avance.Navigation(a => a.Evidencias)
                .HasField("_evidencias")
                .UsePropertyAccessMode(PropertyAccessMode.Field);
        });

        builder.Navigation(p => p.Avances)
            .HasField("_avances")
            .UsePropertyAccessMode(PropertyAccessMode.Field);

        // Costos y reversos.
        builder.OwnsMany(p => p.Costos, costo =>
        {
            costo.ToTable("costos_proyecto");
            costo.WithOwner().HasForeignKey("ProyectoId");

            costo.HasKey(c => c.Id);
            costo.Property(c => c.Id).ValueGeneratedNever();

            costo.Property(c => c.Monto).HasPrecision(18, 2);

            costo.Ignore(c => c.EsReverso);
            costo.Ignore(c => c.MontoConSigno);
        });

        builder.Navigation(p => p.Costos)
            .HasField("_costos")
            .UsePropertyAccessMode(PropertyAccessMode.Field);

        // Referencias de documentos.
        builder.OwnsMany(p => p.Documentos, documento =>
        {
            documento.ToTable("documentos_proyecto");
            documento.WithOwner().HasForeignKey("ProyectoId");

            documento.HasKey(d => d.Id);
            documento.Property(d => d.Id).ValueGeneratedNever();

            documento.Property(d => d.Tipo)
                .HasConversion<string>()
                .HasMaxLength(40);
        });

        builder.Navigation(p => p.Documentos)
            .HasField("_documentos")
            .UsePropertyAccessMode(PropertyAccessMode.Field);

        // Historial de cambios presupuestales.
        builder.OwnsMany(p => p.CambiosPresupuestales, cambio =>
        {
            cambio.ToTable("cambios_presupuestales");
            cambio.WithOwner().HasForeignKey("ProyectoId");

            cambio.HasKey(c => c.Id);
            cambio.Property(c => c.Id).ValueGeneratedNever();

            cambio.Property(c => c.Estado)
                .HasConversion<string>()
                .HasMaxLength(40);

            cambio.Property(c => c.PresupuestoAnterior).HasPrecision(18, 2);
            cambio.Property(c => c.PresupuestoNuevo).HasPrecision(18, 2);

            cambio.OwnsMany(c => c.NuevosAportes, aporte =>
            {
                aporte.ToTable("aportes_cambio_presupuestal");
                aporte.WithOwner().HasForeignKey("CambioId");

                aporte.Property<int>("Id").ValueGeneratedOnAdd();
                aporte.HasKey("CambioId", "Id");

                aporte.Property(a => a.Monto).HasPrecision(18, 2);
            });

            cambio.Navigation(c => c.NuevosAportes)
                .HasField("_nuevosAportes")
                .UsePropertyAccessMode(PropertyAccessMode.Field);
        });

        builder.Navigation(p => p.CambiosPresupuestales)
            .HasField("_cambios")
            .UsePropertyAccessMode(PropertyAccessMode.Field);

        // Alertas propias del proyecto.
        builder.OwnsMany(p => p.Alertas, alerta =>
        {
            alerta.ToTable("alertas_proyecto");
            alerta.WithOwner().HasForeignKey("ProyectoId");

            alerta.HasKey(a => a.Id);
            alerta.Property(a => a.Id).ValueGeneratedNever();

            alerta.Property(a => a.Tipo)
                .HasConversion<string>()
                .HasMaxLength(40);

            alerta.Property(a => a.Estado)
                .HasConversion<string>()
                .HasMaxLength(40);

            alerta.Ignore(a => a.EstaActiva);
        });

        builder.Navigation(p => p.Alertas)
            .HasField("_alertas")
            .UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}