using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace LockedEntityFunction;

public class Functions
{
    private readonly ILogger<Functions> _logger;

    public Functions(ILogger<Functions> logger)
    {
        _logger = logger;
    }

    [FunctionName(nameof(A_FakeProcessingActivity))]
    public async Task A_FakeProcessingActivity(
        [ActivityTrigger] int waitTimeInMilliseconds = 1000)
    {
        await Task.Delay(waitTimeInMilliseconds);
    }

    [FunctionName(nameof(L_Lock))]
    public void L_Lock([EntityTrigger] IDurableEntityContext ctx)
    {
    }
    
    
    [FunctionName(nameof(O_ContinueAsyncWithLock))]
    public async Task O_ContinueAsyncWithLock(
        [OrchestrationTrigger] IDurableOrchestrationContext context)
    {
        var durableLogger = context.CreateReplaySafeLogger(_logger);

        var requestParameters = context.GetInput<ReproParameters>();

        var lockId = new EntityId(nameof(L_Lock), "shared-lock");

        if (requestParameters.Round > 0)
        {
            using (await context.LockAsync(lockId))
            {
                durableLogger.LogInformation(
                    "{functionName} secured lock {lockName} by {instanceId}.",
                    nameof(O_ContinueAsyncWithLock), $"{lockId.EntityName}@{lockId.EntityKey}", context.InstanceId);

                await context.CallActivityWithRetryAsync(nameof(A_FakeProcessingActivity),
                    new RetryOptions(TimeSpan.FromSeconds(10), 3),
                    requestParameters.WaitTimeInMilliSeconds);
            }

            durableLogger.LogInformation(
                "{functionName} {lockName} released by instance {instance ID}.",
                nameof(O_ContinueAsyncWithLock), $"{lockId.EntityName}@{lockId.EntityKey}", context.InstanceId);

            context.ContinueAsNew(new ReproParameters { Round = requestParameters.Round - 1 });
        }
    }

    [FunctionName(nameof(TH_StartEntityLockReproSession))]
    public async Task<IActionResult> TH_StartEntityLockReproSession(
        [HttpTrigger(AuthorizationLevel.Function, "post")] HttpRequest req,
        [DurableClient] IDurableClient starter)
    {
        _logger.LogInformation("{FunctionName} triggered.", nameof(TH_StartEntityLockReproSession));
        
        ReproParameters requestParameters;
            
        try
        {
            requestParameters = await JsonSerializer.DeserializeAsync<ReproParameters>(req.Body);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "{FunctionName} could not initialize", nameof(TH_StartEntityLockReproSession));
            return new BadRequestObjectResult("Invalid parameters in the request.");
        }
        
        var instanceId = await starter.StartNewAsync(nameof(O_ContinueAsyncWithLock), requestParameters);

        _logger.LogInformation("{StartEntityLockReproSession} Started orchestration with ID = '{instanceId}'.", nameof(TH_StartEntityLockReproSession), instanceId);

        return starter.CreateCheckStatusResponse(req, instanceId);
    }
}

public class ReproParameters
{
    public int Round { get; set; }

    public int WaitTimeInMilliSeconds { get; set; } = 1000;
}
