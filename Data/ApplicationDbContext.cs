using Microsoft.EntityFrameworkCore;
using SOPMSApp.Migrations;
using SOPMSApp.Models;

namespace SOPMSApp.Data
{
    public class ApplicationDbContext : DbContext
    {
        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
            : base(options)
        {
        }

        public DbSet<DocRegister> DocRegisters { get; set; }
        public DbSet<StructuredSopHistories> StructuredSopHistory { get; set; }
        public DbSet<SopStepHistories> SopStepHistory { get; set; }
        public DbSet<DocArchive> DocArchives { get; set; }

        public DbSet<DeletedFileLog> DeletedFileLogs { get; set; }

        public DbSet<SopStep> SopSteps { get; set; }

        public DbSet<StructuredSop> StructuredSops { get; set; }

        public DbSet<Area> Areas { get; set; }

        public DbSet<DocRegisterHistory> DocRegisterHistories { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            // Configure one-to-many relationship: StructuredSop has many Steps
            modelBuilder.Entity<StructuredSop>()
                .HasMany(s => s.Steps)
                .WithOne(step => step.StructuredSop)
                .HasForeignKey(step => step.StructuredSopId)
                .OnDelete(DeleteBehavior.Cascade);

            base.OnModelCreating(modelBuilder);
        }

    }

    public class AppDbContext : DbContext
    {
        public AppDbContext(DbContextOptions<AppDbContext> options): base(options){}
        public DbSet<users> users { get; set; }  // Adjust class name capitalization and type
    }

    public class entTTSAPDbContext : DbContext
    {
        public entTTSAPDbContext(DbContextOptions<entTTSAPDbContext> options)
            : base(options)
        {
        }

        public DbSet<LaborUserInfo> Labor { get; set; }

        public DbSet<Documents> Bulletin { get; set; }

        public DbSet<DepartmentModel> Department { get; set; }

        public DbSet<Area> Areas { get; set; }
    }
}
