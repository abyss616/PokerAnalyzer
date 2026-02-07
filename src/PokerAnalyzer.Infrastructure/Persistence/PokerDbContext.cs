using Microsoft.EntityFrameworkCore;

namespace PokerAnalyzer.Infrastructure.Persistence
{
    public class PokerDbContext(DbContextOptions<PokerDbContext> options) : DbContext(options)
    {
        public DbSet<HandHistorySession> Sessions => Set<HandHistorySession>();
        public DbSet<Hand> Hands => Set<Hand>();
        public DbSet<PlayerProfile> PlayerProfiles => Set<PlayerProfile>();
        public DbSet<Hand> HandHistoryHands { get; set; } = null!;
        public DbSet<PositionStats> PositionStats => Set<PositionStats>();
        public DbSet<HandPlayer> HandPlayers => Set<HandPlayer>();
        public DbSet<ShowdownHand> ShowdownHands => Set<ShowdownHand>();
        protected override void OnModelCreating(ModelBuilder b)
        {

            // ---- HandHistorySession ----
            b.Entity<HandHistorySession>()
                .HasIndex(x => x.Id)
                .IsUnique(false);

            b.Entity<HandHistorySession>()
                .HasIndex(x => x.ContentSha256)
                .IsUnique();

            b.Entity<HandHistorySession>()
                .Property(x => x.RawXml);

            // Relationship: Session -> PlayerProfiles (1-to-many)
            b.Entity<HandHistorySession>()
                .HasMany(s => s.Players)
                .WithOne(o => o.Session)
                .HasForeignKey(o => o.SessionId)
                .OnDelete(DeleteBehavior.Cascade);

            // ---- PlayerProfile ----
            b.Entity<PlayerProfile>()
                .HasKey(x => x.Id);

            b.Entity<PlayerProfile>()
                .Property(x => x.Player)
                .IsRequired()
                .HasMaxLength(128);

            // Ensure 1 profile per player per session
            b.Entity<PlayerProfile>()
                .HasIndex(x => new { x.SessionId, x.Player })
                .IsUnique();

            b.Entity<PlayerProfile>()
                .OwnsOne(o => o.PreflopModel);

            b.Entity<PlayerProfile>()
                .OwnsOne(o => o.FlopModel);

            b.Entity<PositionStats>()
                .Property(x => x.Position)
                .HasConversion<int>()      // explicit, future-proof
                .IsRequired();

            // One row per position per opponent profile
            b.Entity<PositionStats>()
                .HasIndex(x => new { x.PlayerProfileId, x.Position })
                .IsUnique();
            b.Entity<HandAction>()
                .HasKey(x => x.Id);

            b.Entity<Hand>()
                .HasMany(h => h.Actions)
                .WithOne(a => a.Hand)
                .HasForeignKey(a => a.HandId)
                .OnDelete(DeleteBehavior.Cascade);

            b.Entity<HandAction>()
                .Property(x => x.Player)
                .IsRequired()
                .HasMaxLength(128);

            b.Entity<HandPlayer>()
                .HasKey(x => x.Id);

            b.Entity<Hand>()
                .HasMany(h => h.Players)
                .WithOne(p => p.Hand)
                .HasForeignKey(p => p.HandId)
                .OnDelete(DeleteBehavior.Cascade);

            b.Entity<HandPlayer>()
                .Property(x => x.Name)
                .IsRequired()
                .HasMaxLength(128);

            // Optional but strongly recommended: one seat per hand
            b.Entity<HandPlayer>()
                .HasIndex(x => new { x.HandId, x.Seat })
                .IsUnique();

            b.Entity<PlayerHandSummary>()
                .HasKey(x => x.Id);

            b.Entity<Hand>()
                .HasMany(h => h.PlayerSummaries)
                .WithOne(s => s.Hand)
                .HasForeignKey(s => s.HandId)
                .OnDelete(DeleteBehavior.Cascade);

            b.Entity<PlayerHandSummary>()
                .Property(x => x.Player)
                .IsRequired()
                .HasMaxLength(128);

            // Optional: one summary row per player per hand
            b.Entity<PlayerHandSummary>()
                .HasIndex(x => new { x.HandId, x.Player })
                .IsUnique();

            b.Entity<ShowdownHand>()
                .HasKey(x => x.Id);

            b.Entity<Hand>()
                .HasMany(h => h.Showdown)
                .WithOne(s => s.Hand)
                .HasForeignKey(s => s.HandId)
                .OnDelete(DeleteBehavior.Cascade);

            b.Entity<ShowdownHand>()
                .Property(x => x.Player)
                .IsRequired()
                .HasMaxLength(128);

            // Optional: avoid duplicates (one row per player per hand)
            b.Entity<ShowdownHand>()
                .HasIndex(x => new { x.HandId, x.Player })
                .IsUnique();
            // Optional: case-insensitive uniqueness in PostgreSQL:
            // Use citext type or functional index lower(player).
            // (Leave as-is unless you want strict case-insensitive uniqueness.)
            b.Entity<Hand>(hand =>
            {
                hand.HasOne(h => h.Board)
                    .WithOne()
                    .HasForeignKey<Board>(b => b.HandId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            b.Entity<Board>(board =>
            {
                board.ToTable("Boards");
                board.HasKey(b => b.Id);

                // FLOP (owned collection)
                board.OwnsMany(b => b.Flop, cb =>
                {
                    cb.ToTable("BoardFlopCards");
                    cb.WithOwner().HasForeignKey("BoardId");

                    cb.Property<int>("Id");
                    cb.HasKey("Id");

                    cb.Property(c => c.Rank).HasConversion<int>();
                    cb.Property(c => c.Suit).HasConversion<int>();
                });

                // TURN (owned single)
                board.OwnsOne(b => b.Turn, cb =>
                {
                    cb.Property(c => c.Rank).HasConversion<int>();
                    cb.Property(c => c.Suit).HasConversion<int>();

                    cb.ToTable("Boards"); // stored inline
                });

                // RIVER (owned single)
                board.OwnsOne(b => b.River, cb =>
                {
                    cb.Property(c => c.Rank).HasConversion<int>();
                    cb.Property(c => c.Suit).HasConversion<int>();

                    cb.ToTable("Boards"); // stored inline
                });
            });




        }

        public override int SaveChanges(bool acceptAllChangesOnSuccess)
        {
            FixHandGraphForeignKeys();
            return base.SaveChanges(acceptAllChangesOnSuccess);
        }

        public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
        {
            FixHandGraphForeignKeys();
            return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
        }

        private void FixHandGraphForeignKeys()
        {
            // Only look at Hands that are being inserted/updated and are tracked.
            var handEntries = ChangeTracker.Entries<Hand>()
                .Where(e => e.State is EntityState.Added or EntityState.Modified)
                .ToList();

            foreach (var handEntry in handEntries)
            {
                var hand = handEntry.Entity;
                if (hand.Id == Guid.Empty)
                    hand.Id = Guid.NewGuid();

                // Ensure SessionId if you use it
                // (Only if Hand has SessionId and you're saving through Session graph)
                // if (hand.SessionId == Guid.Empty && handEntry.Reference("Session").CurrentValue is HandHistorySession s)
                //     hand.SessionId = s.Id;

                if (hand.Actions != null)
                    foreach (var a in hand.Actions)
                        if (a.HandId == Guid.Empty) a.HandId = hand.Id;

                if (hand.Players != null)
                    foreach (var p in hand.Players)
                        if (p.HandId == Guid.Empty) p.HandId = hand.Id;

                if (hand.Showdown != null)
                    foreach (var s in hand.Showdown)
                        if (s.HandId == Guid.Empty) s.HandId = hand.Id;
            }

            // Also cover cases where child entities are added directly without the Hand being modified/added
            FixOrphanChildren<HandAction>(h => h.HandId);
            FixOrphanChildren<HandPlayer>(h => h.HandId);
            FixOrphanChildren<ShowdownHand>(h => h.HandId);
        }

        private void FixOrphanChildren<T>(Func<T, Guid> fkGetter) where T : class
        {
            var entries = ChangeTracker.Entries<T>()
                .Where(e => e.State == EntityState.Added)
                .ToList();

            foreach (var entry in entries)
            {
                // If FK is empty, try to set it from the principal if the principal is tracked
                // This requires that the child is reachable via a tracked Hand navigation graph
                // In a one-direction-only model, this second pass can't always infer the Hand.
                // So the main loop over Hand entries is the reliable part.
            }
        }

    }
}