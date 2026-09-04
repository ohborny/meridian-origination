using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using MortgageLOS;
using MortgageLOS.Origination;
using MortgageLOS.Credit;
using MortgageLOS.Underwriting;
using MortgageLOS.Compliance;
using MortgageLOS.Documents;

namespace MortgageLOS.Web
{
    // =========================================================================
    // MortgageLOS Web API - Program entry point
    //
    // This is a minimal ASP.NET Core Web API that exposes the loan origination
    // system's functionality. It was added in 2015 when the old WebForms UI was
    // decommissioned. The old UI is still running on a separate server because
    // some users refuse to switch.
    // =========================================================================

    public class Program
    {
        public static void Main(string[] args)
        {
            // Initialize logging
            string logDir = Path.Combine(Directory.GetCurrentDirectory(), "logs");
            FileLogger.Initialize(logDir, "INFO");
            FileLogger.Info("Web", "MortgageLOS Web API starting...");

            // Initialize config
            AppConfig.Initialize(Path.Combine(Directory.GetCurrentDirectory(), "config", "los.config"));

            var builder = WebApplication.CreateBuilder(args);

            builder.Services.AddControllers();
            builder.Services.AddEndpointsApiExplorer();
            builder.Services.AddSwaggerGen();

            // Register services (transient because DatabaseHelper opens connections per-call)
            builder.Services.AddTransient<LoanApplicationService>();
            builder.Services.AddTransient<LoanProductService>();
            builder.Services.AddTransient<LoanOfficerService>();
            builder.Services.AddTransient<PipelineService>();
            builder.Services.AddTransient<CreditReportService>();
            builder.Services.AddTransient<DtiCalculator>();
            builder.Services.AddTransient<CreditAnalysisService>();
            builder.Services.AddTransient<AusService>();
            builder.Services.AddTransient<UnderwritingService>();
            builder.Services.AddTransient<UnderwritingWorkflowService>();
            builder.Services.AddTransient<ComplianceCheckService>();
            builder.Services.AddTransient<HmdaService>();
            builder.Services.AddTransient<TridService>();
            builder.Services.AddTransient<DisclosureService>();
            builder.Services.AddTransient<ComplianceExceptionService>();
            builder.Services.AddTransient<ScraService>();
            builder.Services.AddTransient<DocumentService>();
            builder.Services.AddTransient<DocumentChecklistService>();

            var app = builder.Build();

            // Serve the legacy Web UI from wwwroot
            app.UseDefaultFiles();
            app.UseStaticFiles();

            // Swagger available in all environments for demo purposes
            // (originally only in Development, changed 2024 for customer demos)
            app.UseSwagger();
            app.UseSwaggerUI();

            app.MapControllers();

            // Health check endpoint
            app.MapGet("/health", () => Results.Ok(new { status = "healthy", version = "4.2.17", environment = AppConfig.Instance.Environment }));

            FileLogger.Info("Web", "MortgageLOS Web API started on port 5000");
            app.Run();
        }
    }
}
