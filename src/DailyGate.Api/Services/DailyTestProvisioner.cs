using System.Security.Cryptography;
using System.Text.Json;
using DailyGate.Api.Data;
using DailyGate.Api.Domain;
using DailyGate.Api.Infrastructure;
using DailyGate.Shared;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace DailyGate.Api.Services;

public sealed class DailyTestProvisioner(
    DailyGateDbContext db,
    WorkdayService workdayService,
    ServerSigningService signingService)
{
    public async Task EnsureWindowAsync(Guid employeeId, int days, CancellationToken cancellationToken = default)
    {
        var employee = await db.Employees.AsNoTracking()
            .SingleAsync(x => x.Id == employeeId, cancellationToken);
        if (employee.State != EmployeeState.Active) return;

        var firstDay = workdayService.Current();
        for (var offset = 0; offset < days; offset++)
        {
            var day = firstDay.AddDays(offset);
            if (await db.DailyTestInstances.AnyAsync(
                    x => x.EmployeeId == employeeId && x.Workday == day, cancellationToken)) continue;

            var rule = await db.TestRules
                .Include(x => x.QuestionBank).ThenInclude(x => x.Questions).ThenInclude(x => x.Options)
                .Where(x => x.Active && x.EffectiveFrom <= day
                    && (x.EmployeeGroupId == employee.GroupId || x.EmployeeGroupId == null)
                    && x.QuestionBank.Published)
                .OrderByDescending(x => x.EmployeeGroupId == employee.GroupId)
                .ThenByDescending(x => x.EffectiveFrom)
                .ThenByDescending(x => x.CreatedAt)
                .FirstOrDefaultAsync(cancellationToken);
            if (rule is null) continue;

            var available = rule.QuestionBank.Questions.Where(x => x.Active).ToList();
            if (available.Count < rule.QuestionCount) continue;

            Shuffle(available);
            var selected = available.Take(rule.QuestionCount)
                .Select(question =>
                {
                    var options = question.Options.ToList();
                    Shuffle(options);
                    return new DailyQuestion(
                        question.Id,
                        question.Text,
                        question.Type == QuestionType.SingleChoice
                            ? QuestionKind.SingleChoice : QuestionKind.MultipleChoice,
                        true,
                        options.Select(x => new DailyOption(x.Id, x.Text)).ToArray());
                }).ToArray();

            var instanceId = Guid.NewGuid();
            var payload = new DailyTestPayload(
                instanceId,
                employeeId,
                day,
                rule.Name,
                rule.TimeLimitMinutes,
                DateTimeOffset.UtcNow,
                selected);
            var json = JsonSerializer.Serialize(payload, JsonDefaults.Options);
            var instance = new DailyTestInstance
            {
                Id = instanceId,
                EmployeeId = employeeId,
                TestRuleId = rule.Id,
                Workday = day,
                PayloadJson = json,
                PayloadSignature = signingService.Sign(json)
            };
            db.DailyTestInstances.Add(instance);
            try
            {
                await db.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException exception) when
                (exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
            {
                // API sync and the background worker may provision the same workday concurrently.
                // The database uniqueness constraint is authoritative; keep the winner and continue.
                db.Entry(instance).State = EntityState.Detached;
            }
        }
    }

    private static void Shuffle<T>(IList<T> items)
    {
        for (var index = items.Count - 1; index > 0; index--)
        {
            var swap = RandomNumberGenerator.GetInt32(index + 1);
            (items[index], items[swap]) = (items[swap], items[index]);
        }
    }
}
