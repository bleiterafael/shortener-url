using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using UrlShortener.Api.Domain;

namespace UrlShortener.Api.Data.Configurations;

public sealed class ShortenedLinkConfiguration : IEntityTypeConfiguration<ShortenedLink>
{
    public void Configure(EntityTypeBuilder<ShortenedLink> builder)
    {
        builder.ToTable("ShortenedLinks");

        builder.HasKey(x => x.Id)
            .HasName("PK_ShortenedLinks");

        builder.Property(x => x.Code)
            .IsRequired()
            .HasMaxLength(7)
            .IsFixedLength();

        builder.HasIndex(x => x.Code, "IX_ShortenedLinks_Code")
            .IsUnique();

        builder.Property(x => x.OriginalUrl)
            .IsRequired()
            .HasMaxLength(2048);

        builder.Property(x => x.ClickCount)
            .IsRequired()
            .HasDefaultValue(0L);

        builder.Property(x => x.ManagementKeyHash)
            .IsRequired()
            .HasMaxLength(64)
            .IsFixedLength();

        builder.Property(x => x.CreatedAtUtc)
            .IsRequired();
    }
}

