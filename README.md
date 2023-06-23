# Failing Locked Entity Repro

## Abstract

The goal of this repository is to provide the sources and steps to reproduce a behavior we have observed in one of the projects we are working on.

When using an Eternal Orchestration and a durable entity with distributed tracing enabled, it causes a storage exception over time.

## Setup

### Requirements

You will to have installed:
* [.NET 6](https://dotnet.microsoft.com/en-us/download/dotnet/6.0)
* [Azure function Core tools](https://learn.microsoft.com/en-us/azure/azure-functions/functions-run-local?tabs=v4%2Cmacos%2Ccsharp%2Cportal%2Cbash#v2)

For more information on the setup, refer to [this documentation](https://learn.microsoft.com/en-us/azure/azure-functions/functions-develop-local).

## Install

You will first need to create a `local.settings.json` file inside [`LockedEntityFunction`](./LockedEntityFunction) with the default content:
```json
{
    "IsEncrypted": false,
    "Values": {
        "AzureWebJobsStorage": "UseDevelopmentStorage=true",
        "FUNCTIONS_WORKER_RUNTIME": "dotnet"
    }
}
```

For ease of use, you can set up the connection string of a storage account from Azure instead of using `UseDevelopmentStorage=true`

Although it might work with the development storage, I have not tested it.

## Reproducing steps

From the command line or from your IDE, run the `LockedEntity` Azure function.

Using Postman, CURL or any tool you are familiar with, make a post request to `<you app URL>/api/TH_StartEntityLockReproSession` with the parameters:
```json
{
    "Round": 1000,
    "WaitTimeInMilliSeconds": 1
}
```

Any value for the round and wait time would be okay. Be careful, as the higher the round and the wait time, the longer the function will run.

After waiting for a few minutes, you can go to the tables of your storage account. You can Search for the `lockedentityreproHistory` table.
In that table, search for an entry with the name `@l_lock@shared-lock` and the `EventType` `ExecutionStarted`.

Next, take a look at the `Correlation` field which grows as the execution progresses.

Eventually the function will throw an exception when attempting to lock the entity with the following message:
```
The property value exceeds the maximum allowed size (64KB). If the property value is a string, it is UTF-16 encoded and the maximum number of characters should be 32K or less.
```

### Notes

We believe the error is caused by the `Correlation` field making the overall table entry go over the 32K limitation.

From our testing it appears that removing the distributed inside [`host.json`](./LockedEntityFunction/host.json) login solves the issue but it prevents the use of distributed login for the function