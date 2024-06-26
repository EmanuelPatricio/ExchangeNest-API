﻿using Application.Applications;
using Application.Applications.Cancel;
using Application.Applications.Close;
using Application.Applications.Get;
using Application.Applications.Publish;
using Application.Applications.Update;
using Application.Shared.Queries.GetNewId;
using Carter;
using Domain.Applications;
using Domain.ExchangePrograms;
using Domain.Users;
using Infrastructure.Abstractions.Authentication;
using MediatR;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.CodeAnalysis;
using System.Security.Claims;
using static Domain.Shared.Enums;

namespace WebApi.Endpoints.Applications;

public class ApplicationsEndPoints : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("api/applications").WithTags("Application").RequireAuthorization();

        group.MapPost("", PublishApplication);
        group.MapGet("/{applicationId:int}", GetById);
        group.MapGet("", GetAll);
        group.MapPut("", Update);
        group.MapPut("/cancel", Cancel);
        group.MapPut("/close", Close);
        group.MapDelete("/{documentId:int}", DeleteDocument);
    }

    public static async Task<Results<Created, UnprocessableEntity<string>, BadRequest<string>>> PublishApplication(PublishApplicationRequest request, ISender sender)
    {
        try
        {
            var query = new GetNewIdQuery("Id", "Applications");

            var id = await sender.Send(query);

            if (id is null)
            {
                return TypedResults.UnprocessableEntity("Couldn't get id for the new application");
            }

            query = new GetNewIdQuery("Id", "ApplicationDocuments");

            var newDocumentId = await sender.Send(query);

            if (newDocumentId is null)
            {
                return TypedResults.UnprocessableEntity("Couldn't get id for the new application document");
            }

            var applicationDocuments = new List<ApplicationDocumentValues>();
            foreach (var applicationDocument in request.ApplicationDocuments)
            {
                var applicationDocumentNewId = applicationDocuments.Count > 0 ? applicationDocuments.Last().Id : newDocumentId.Value - 1;

                applicationDocuments.Add(new ApplicationDocumentValues(++applicationDocumentNewId, applicationDocument.Category, applicationDocument.Url, applicationDocument.StatusId, applicationDocument.Reason));
            }

            var requiredDocuments = new List<ApplicationDocumentValues>();
            foreach (var requiredDocument in request.RequiredDocuments)
            {
                var requiredDocumentNewId = applicationDocuments.Count > 0 ? applicationDocuments.Last().Id : newDocumentId.Value - 1;
                requiredDocumentNewId = requiredDocuments.Count > 0 ? requiredDocuments.Last().Id : requiredDocumentNewId;

                requiredDocuments.Add(new ApplicationDocumentValues(++requiredDocumentNewId, requiredDocument.Category, requiredDocument.Url, requiredDocument.StatusId, requiredDocument.Reason));
            }

            var command = new PublishApplicationCommand(
                id.Value,
                request.ProgramId,
                request.StudentId,
                request.Reason,
                request.StatusId,
                applicationDocuments,
                requiredDocuments,
                request.Url);

            var result = await sender.Send(command);

            if (result.IsFailure)
            {
                return TypedResults.UnprocessableEntity(result.Error.Name);
            }

            return TypedResults.Created();
        }
        catch (Exception e)
        {
            return TypedResults.BadRequest(e.Message);
        }
    }

    public static async Task<Results<Ok<GetApplicationResponse>, NotFound, BadRequest<string>>> GetById(int applicationId, IApplicationRepository applicationRepository)
    {
        try
        {
            var application = await applicationRepository.GetById(new Domain.Applications.ApplicationId(applicationId));

            if (application is null)
            {
                return TypedResults.NotFound();
            }

            var applicationDocuments = application.Documents
                        .Where(x => x.DocumentType == (int)DocumentTypes.Application)
                        .Select(x => new ApplicationDocumentValues(x.Id.Value, x.DocumentCategory, x.DocumentUrl, x.StatusId, x.Reason))
                        .ToList();

            var requiredDocuments = application.Documents
                        .Where(x => x.DocumentType == (int)DocumentTypes.Required)
                        .Select(x => new ApplicationDocumentValues(x.Id.Value, x.DocumentCategory, x.DocumentUrl, x.StatusId, x.Reason))
                        .ToList();

            var response = new GetApplicationResponse(
                    application.Id.Value,
                    application.ProgramId,
                    application.StudentId,
                    application.Reason.Value,
                    application.StatusId,
                    applicationDocuments,
                    requiredDocuments);

            return TypedResults.Ok(response);
        }
        catch (Exception e)
        {
            return TypedResults.BadRequest(e.Message);
        }
    }

    public static async Task<Results<Ok<List<GetApplicationResponse>>, UnauthorizedHttpResult, NotFound, BadRequest<string>>> GetAll(IApplicationRepository applicationRepository, IUserRepository userRepository, IExchangeProgramRepository exchangeProgramRepository, HttpContext httpContext)
    {
        try
        {
            var applicationsResponse = new List<GetApplicationResponse>();

            var applicationsList = await applicationRepository.GetAll();

            if (applicationsList is null || applicationsList.Count == 0)
            {
                return TypedResults.NotFound();
            }

            var identity = httpContext.User.Identity as ClaimsIdentity;

            if (identity is null)
            {
                return TypedResults.Unauthorized();
            }

            if (identity.FindFirst(CustomClaim.UserId) is null)
            {
                return TypedResults.Unauthorized();
            }

            User? user = null;

            if (int.TryParse(identity.FindFirst(CustomClaim.UserId)?.Value, out var userId))
            {
                user = await userRepository.GetById(new(userId));

                if (user is null)
                {
                    return TypedResults.Unauthorized();
                }
            }
            else
            {
                return TypedResults.Unauthorized();
            }

            if (user.OrganizationId != 0)
            {
                var exchangePrograms = await exchangeProgramRepository.GetAll();

                var exchangeProgramsIds = exchangePrograms.Where(x => x.OrganizationId == user.OrganizationId).Select(x => x.Id.Value).ToList();

                applicationsList = (Roles)user.RoleId switch
                {
                    Roles.Administrator => applicationsList,
                    Roles.Organization => applicationsList.Where(x => exchangeProgramsIds.Contains(x.ProgramId)).ToList(),
                    _ => applicationsList.Where(x => x.StudentId == userId && x.StatusId != (int)Statuses.Deleted).ToList()
                };
            }
            else
            {
                applicationsList = (Roles)user.RoleId switch
                {
                    Roles.Administrator or Roles.Organization => applicationsList,
                    _ => applicationsList.Where(x => x.StudentId == userId && x.StatusId != (int)Statuses.Deleted).ToList()
                };
            }

            foreach (var application in applicationsList)
            {
                var applicationDocuments = application.Documents
                        .Where(x => x.DocumentType == (int)DocumentTypes.Application)
                        .Select(x => new ApplicationDocumentValues(x.Id.Value, x.DocumentCategory, x.DocumentUrl, x.StatusId, x.Reason))
                        .ToList();

                var requiredDocuments = application.Documents
                            .Where(x => x.DocumentType == (int)DocumentTypes.Required)
                            .Select(x => new ApplicationDocumentValues(x.Id.Value, x.DocumentCategory, x.DocumentUrl, x.StatusId, x.Reason))
                            .ToList();

                var response = new GetApplicationResponse(
                        application.Id.Value,
                        application.ProgramId,
                        application.StudentId,
                        application.Reason.Value,
                        application.StatusId,
                        applicationDocuments,
                        requiredDocuments);


                applicationsResponse.Add(response);
            }

            return TypedResults.Ok(applicationsResponse);
        }
        catch (Exception e)
        {
            return TypedResults.BadRequest(e.Message);
        }
    }

    public static async Task<Results<Ok, UnprocessableEntity<string>, BadRequest<string>, UnauthorizedHttpResult>> Update(UpdateApplicationRequest request, ISender sender, HttpContext httpContext)
    {
        try
        {
            int userId;

            var identity = httpContext.User.Identity as ClaimsIdentity;

            if (identity is null)
            {
                return TypedResults.Unauthorized();
            }

            if (identity.FindFirst(CustomClaim.UserId) is null)
            {
                return TypedResults.Unauthorized();
            }

            if (int.TryParse(identity.FindFirst(CustomClaim.UserId)?.Value, out var id))
            {
                userId = id;
            }
            else
            {
                return TypedResults.Unauthorized();
            }

            if (request.StatusId == (int)Statuses.Cancelled)
            {
                await Cancel(new(request.Id, string.Empty), sender);

                return TypedResults.Ok();
            }

            if (request.StatusId == (int)Statuses.Closed)
            {
                await Close(new(request.Id, string.Empty), sender);

                return TypedResults.Ok();
            }

            var query = new GetNewIdQuery("Id", "ApplicationDocuments");

            var newDocumentId = await sender.Send(query);

            if (newDocumentId is null)
            {
                return TypedResults.UnprocessableEntity("Couldn't get id for the new application document");
            }

            var applicationDocuments = new List<ApplicationDocumentValues>();
            foreach (var applicationDocument in request.ApplicationDocuments)
            {
                if (applicationDocument.Id > 0)
                {
                    applicationDocuments.Add(applicationDocument);
                    continue;
                }

                var applicationDocumentNewId = applicationDocuments.Any() switch
                {
                    true => applicationDocuments.Max(x => x.Id) + 1,
                    false => newDocumentId.Value
                };

                applicationDocuments.Add(new(applicationDocumentNewId, applicationDocument.Category, applicationDocument.Url, applicationDocument.StatusId, applicationDocument.Reason));
            }

            var requiredDocuments = new List<ApplicationDocumentValues>();
            foreach (var requiredDocument in request.RequiredDocuments)
            {
                if (requiredDocument.Id > 0)
                {
                    requiredDocuments.Add(requiredDocument);
                    continue;
                }

                var requiredDocumentNewId = requiredDocuments.Any() switch
                {
                    true => requiredDocuments.Max(x => x.Id) + 1,
                    false => applicationDocuments.Any() ? applicationDocuments.Max(x => x.Id) + 1 : 1
                };

                requiredDocuments.Add(new(requiredDocumentNewId, requiredDocument.Category, requiredDocument.Url, requiredDocument.StatusId, requiredDocument.Reason));
            }

            var command = new UpdateApplicationCommand(
                request.Id,
                request.Reason,
                request.StatusId,
                applicationDocuments,
                requiredDocuments,
                newDocumentId.Value - 1,
                request.Url,
                userId);

            var result = await sender.Send(command);

            if (result.IsFailure)
            {
                return TypedResults.UnprocessableEntity(result.Error.Name);
            }

            return TypedResults.Ok();
        }
        catch (Exception e)
        {
            return TypedResults.BadRequest(e.Message);
        }
    }

    public static async Task<Results<Ok, UnprocessableEntity<string>, BadRequest<string>>> Cancel(CancelApplicationRequest request, ISender sender)
    {
        try
        {
            var command = new CancelApplicationCommand(request.Id, request.Reason);

            var result = await sender.Send(command);

            if (result.IsFailure)
            {
                return TypedResults.UnprocessableEntity(result.Error.Name);
            }

            return TypedResults.Ok();
        }
        catch (Exception e)
        {
            return TypedResults.BadRequest(e.Message);
        }
    }

    public static async Task<Results<Ok, UnprocessableEntity<string>, BadRequest<string>>> Close(CloseApplicationRequest request, ISender sender)
    {
        try
        {
            var command = new CloseApplicationCommand(request.Id, request.Reason);

            var result = await sender.Send(command);

            if (result.IsFailure)
            {
                return TypedResults.UnprocessableEntity(result.Error.Name);
            }

            return TypedResults.Ok();
        }
        catch (Exception e)
        {
            return TypedResults.BadRequest(e.Message);
        }
    }

    public static Results<Ok, UnprocessableEntity<string>, BadRequest<string>> DeleteDocument(int documentId, IApplicationRepository applicationRepository)
    {
        try
        {
            applicationRepository.DeleteDocument(new(documentId));

            return TypedResults.Ok();
        }
        catch (Exception e)
        {
            return TypedResults.BadRequest(e.Message);
        }
    }
}
