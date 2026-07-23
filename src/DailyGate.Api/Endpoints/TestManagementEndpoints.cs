using DailyGate.Api.Contracts;
using DailyGate.Api.Data;
using DailyGate.Api.Domain;
using DailyGate.Api.Services;
using Microsoft.EntityFrameworkCore;

namespace DailyGate.Api.Endpoints;

public static class TestManagementEndpoints
{
    public static IEndpointRouteBuilder MapTestManagementEndpoints(this IEndpointRouteBuilder app)
    {
        var banks = app.MapGroup("/api/v1/admin/test-banks").WithTags("Test management")
            .RequireAuthorization("AdminOnly");
        banks.MapGet("/", async (DailyGateDbContext db) => Results.Ok(await db.QuestionBanks.AsNoTracking()
            .OrderByDescending(x => x.CreatedAt).Select(x => new
            {
                x.Id, x.Name, x.Description, x.Version, x.Published, x.PublishedAt,
                questionCount = x.Questions.Count,
                activeQuestionCount = x.Questions.Count(q => q.Active)
            }).ToListAsync()));
        banks.MapGet("/{id:guid}", async (Guid id, DailyGateDbContext db) =>
        {
            var bank = await db.QuestionBanks.AsNoTracking().Include(x => x.Questions).ThenInclude(x => x.Options)
                .SingleOrDefaultAsync(x => x.Id == id);
            return bank is null ? Results.NotFound() : Results.Ok(new
            {
                bank.Id, bank.Name, bank.Description, bank.Version, bank.Published, bank.PublishedAt,
                questions = bank.Questions.OrderBy(x => x.SortOrder).Select(x => new
                {
                    x.Id, x.Text, type = x.Type.ToString(), x.Active,
                    options = x.Options.OrderBy(o => o.SortOrder).Select(o => new { o.Id, o.Text })
                })
            });
        });
        banks.MapPost("/", async (CreateBankRequest request, DailyGateDbContext db, AuditService audit) =>
        {
            if (string.IsNullOrWhiteSpace(request.Name) || request.Name.Length > 200)
                return Results.BadRequest(new { message = "Bank name is required and must not exceed 200 characters." });
            var normalizedName = request.Name.Trim();
            var version = (await db.QuestionBanks.Where(x => x.Name == normalizedName)
                .MaxAsync(x => (int?)x.Version) ?? 0) + 1;
            var bank = new QuestionBank { Name = normalizedName, Description = request.Description?.Trim(), Version = version };
            db.QuestionBanks.Add(bank);
            await db.SaveChangesAsync();
            await audit.WriteAsync("test_bank.created", nameof(QuestionBank), bank.Id, new { bank.Name });
            return Results.Created($"/api/v1/admin/test-banks/{bank.Id}", new { bank.Id });
        });
        banks.MapPost("/{id:guid}/versions", async (Guid id, DailyGateDbContext db, AuditService audit) =>
        {
            var source = await db.QuestionBanks.AsNoTracking().Include(x => x.Questions).ThenInclude(x => x.Options)
                .SingleOrDefaultAsync(x => x.Id == id);
            if (source is null) return Results.NotFound();
            var version = (await db.QuestionBanks.Where(x => x.Name == source.Name)
                .MaxAsync(x => (int?)x.Version) ?? source.Version) + 1;
            var bank = new QuestionBank
            {
                Name = source.Name,
                Description = source.Description,
                Version = version,
                Questions = source.Questions.OrderBy(x => x.SortOrder).Select(question => new Question
                {
                    Text = question.Text,
                    Type = question.Type,
                    Active = question.Active,
                    SortOrder = question.SortOrder,
                    Options = question.Options.OrderBy(x => x.SortOrder).Select(option => new QuestionOption
                    {
                        Text = option.Text,
                        SortOrder = option.SortOrder
                    }).ToList()
                }).ToList()
            };
            db.QuestionBanks.Add(bank);
            await db.SaveChangesAsync();
            await audit.WriteAsync("test_bank.version_created", nameof(QuestionBank), bank.Id,
                new { sourceBankId = source.Id, bank.Version });
            return Results.Created($"/api/v1/admin/test-banks/{bank.Id}", new { bank.Id, bank.Version });
        });
        banks.MapPost("/{id:guid}/questions", async (Guid id, CreateQuestionRequest request,
            DailyGateDbContext db, AuditService audit) =>
        {
            if (string.IsNullOrWhiteSpace(request.Text) || request.Text.Length > 4000)
                return Results.BadRequest(new { message = "Question text is required and must not exceed 4000 characters." });
            var bank = await db.QuestionBanks.Include(x => x.Questions).SingleOrDefaultAsync(x => x.Id == id);
            if (bank is null) return Results.NotFound();
            if (bank.Published) return Results.Conflict(new { message = "Published banks are immutable. Create a new version." });
            var options = (request.Options ?? []).Where(x => x is not null).Select(x => x.Trim())
                .Where(x => x.Length > 0 && x.Length <= 1000).Distinct().ToArray();
            if (options.Length < 2) return Results.BadRequest(new { message = "At least two unique options are required." });
            var question = new Question
            {
                QuestionBankId = id,
                Text = request.Text.Trim(),
                Type = request.Type,
                SortOrder = bank.Questions.Count,
                Options = options.Select((text, index) => new QuestionOption { Text = text, SortOrder = index }).ToList()
            };
            db.Questions.Add(question);
            await db.SaveChangesAsync();
            await audit.WriteAsync("question.created", nameof(Question), question.Id, new { bankId = id, question.Text });
            return Results.Created($"/api/v1/admin/test-banks/{id}/questions/{question.Id}", new { question.Id });
        });
        banks.MapPost("/{id:guid}/publish", async (Guid id, DailyGateDbContext db, AuditService audit) =>
        {
            var bank = await db.QuestionBanks.Include(x => x.Questions).SingleOrDefaultAsync(x => x.Id == id);
            if (bank is null) return Results.NotFound();
            if (bank.Questions.Count(x => x.Active) == 0)
                return Results.BadRequest(new { message = "A bank must contain active questions before publication." });
            bank.Published = true;
            bank.PublishedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync();
            await audit.WriteAsync("test_bank.published", nameof(QuestionBank), bank.Id, new { bank.Version });
            return Results.NoContent();
        });

        var rules = app.MapGroup("/api/v1/admin/test-rules").WithTags("Test management")
            .RequireAuthorization("AdminOnly");
        rules.MapGet("/", async (DailyGateDbContext db) => Results.Ok(await db.TestRules.AsNoTracking()
            .Include(x => x.QuestionBank).Include(x => x.EmployeeGroup)
            .OrderByDescending(x => x.EffectiveFrom).Select(x => new
            {
                x.Id, x.Name, x.QuestionCount, x.TimeLimitMinutes, x.EffectiveFrom, x.Active,
                bank = new { x.QuestionBank.Id, x.QuestionBank.Name, x.QuestionBank.Version },
                group = x.EmployeeGroup == null ? null : new { x.EmployeeGroup.Id, x.EmployeeGroup.Name }
            }).ToListAsync()));
        rules.MapPost("/", async (CreateRuleRequest request, DailyGateDbContext db, AuditService audit) =>
        {
            if (string.IsNullOrWhiteSpace(request.Name) || request.Name.Length > 200)
                return Results.BadRequest(new { message = "Rule name is required and must not exceed 200 characters." });
            if (request.QuestionCount is < 1 or > 100 || request.TimeLimitMinutes is < 1 or > 180)
                return Results.BadRequest(new { message = "Question count or time limit is outside the allowed range." });
            var bank = await db.QuestionBanks.Include(x => x.Questions).SingleOrDefaultAsync(x => x.Id == request.QuestionBankId);
            if (bank is null) return Results.NotFound();
            if (!bank.Published) return Results.BadRequest(new { message = "Question bank must be published." });
            if (bank.Questions.Count(x => x.Active) < request.QuestionCount)
                return Results.BadRequest(new { message = "The bank does not contain enough active questions." });
            var rule = new TestRule
            {
                Name = request.Name.Trim(), QuestionBankId = request.QuestionBankId,
                EmployeeGroupId = request.EmployeeGroupId, QuestionCount = request.QuestionCount,
                TimeLimitMinutes = request.TimeLimitMinutes, EffectiveFrom = request.EffectiveFrom
            };
            db.TestRules.Add(rule);
            await db.SaveChangesAsync();
            await audit.WriteAsync("test_rule.created", nameof(TestRule), rule.Id, request);
            return Results.Created($"/api/v1/admin/test-rules/{rule.Id}", new { rule.Id });
        });
        rules.MapDelete("/{id:guid}", async (Guid id, DailyGateDbContext db, AuditService audit) =>
        {
            var rule = await db.TestRules.FindAsync(id);
            if (rule is null) return Results.NotFound();
            rule.Active = false;
            await db.SaveChangesAsync();
            await audit.WriteAsync("test_rule.disabled", nameof(TestRule), id);
            return Results.NoContent();
        });
        return app;
    }
}
