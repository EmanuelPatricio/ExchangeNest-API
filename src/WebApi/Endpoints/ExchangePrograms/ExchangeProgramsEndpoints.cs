﻿using Application.ExchangePrograms.Close;
using Application.ExchangePrograms.Get;
using Application.ExchangePrograms.Publish;
using Application.ExchangePrograms.Update;
using Application.Shared.Queries.GetNewId;
using Carter;
using Domain.Applications;
using Domain.ExchangePrograms;
using Domain.Users;
using Infrastructure.Abstractions.Authentication;
using MediatR;
using Microsoft.AspNetCore.Http.HttpResults;
using System.Security.Claims;
using static Domain.Shared.Enums;

namespace WebApi.Endpoints.ExchangePrograms;

public class ExchangeProgramsEndpoints : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("api/exchange-programs").WithTags("Exchange Program").RequireAuthorization();

        group.MapPost("", PublishExchangeProgram);
        group.MapGet("/{exchangeProgramId:int}", GetById);
        group.MapGet("", GetAll);
        group.MapPut("", Update);
        group.MapDelete("/{exchangeProgramId:int}", Close);
    }

    public static async Task<Results<Created, UnprocessableEntity<string>, BadRequest<string>>> PublishExchangeProgram(PublishExchangeProgramRequest request, ISender sender)
    {
        try
        {
            var query = new GetNewIdQuery("Id", "ExchangePrograms");

            var id = await sender.Send(query);

            if (id is null)
            {
                return TypedResults.UnprocessableEntity("Couldn't get id for the new exchange program");
            }

            var command = new PublishExchangeProgramCommand(
                id.Value,
                request.Name,
                request.Description,
                request.LimitApplicationDate,
                request.StartDate,
                request.FinishDate,
                request.ApplicationDocuments,
                request.RequiredDocuments,
                request.ImagesUrl,
                request.OrganizationId,
                request.CountryId,
                request.StateId,
                request.StatusId);

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

    public static async Task<Results<Ok<GetExchangeProgramResponse>, NotFound, BadRequest<string>>> GetById(int exchangeProgramId, IExchangeProgramRepository exchangeProgramRepository)
    {
        try
        {
            var exchangeProgram = await exchangeProgramRepository.GetById(new ExchangeProgramId(exchangeProgramId));

            if (exchangeProgram is null)
            {
                return TypedResults.NotFound();
            }

            var response = new GetExchangeProgramResponse(exchangeProgram.Id.Value, exchangeProgram.Name.Value, exchangeProgram.Description.Value, exchangeProgram.LimitApplicationDate.Value, exchangeProgram.StartDate.Value, exchangeProgram.FinishDate.Value, exchangeProgram.ApplicationDocuments.Value, exchangeProgram.RequiredDocuments.Value, exchangeProgram.Images.Value, exchangeProgram.OrganizationId, exchangeProgram.CountryId, exchangeProgram.StateId, exchangeProgram.StatusId);

            return TypedResults.Ok(response);
        }
        catch (Exception e)
        {
            return TypedResults.BadRequest(e.Message);
        }
    }

    public static async Task<Results<Ok<List<GetExchangeProgramResponse>>, UnauthorizedHttpResult, NotFound, BadRequest<string>>> GetAll(IExchangeProgramRepository exchangeProgramRepository, IUserRepository userRepository, IApplicationRepository applicationRepository, HttpContext httpContext)
    {
        try
        {
            var exchangeProgramsResponse = new List<GetExchangeProgramResponse>();

            var exchangeProgramsList = await exchangeProgramRepository.GetAll();

            if (exchangeProgramsList is null || exchangeProgramsList.Count == 0)
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

            var userApplications = await applicationRepository.GetAll();

            userApplications = userApplications.Where(x => x.StudentId == userId).ToList();

            if (userApplications.Any(x => x.StatusId == (int)Statuses.Pending))
            {
                return TypedResults.Ok(new List<GetExchangeProgramResponse>());
            }

            if (user.OrganizationId != 0)
            {
                exchangeProgramsList = (Roles)user.RoleId switch
                {
                    Roles.Administrator => exchangeProgramsList,
                    _ => exchangeProgramsList.Where(x => x.OrganizationId == user.OrganizationId).ToList()
                };
            }

            exchangeProgramsList = exchangeProgramsList.Where(x => x.StatusId != (int)Statuses.Deleted).ToList();

            foreach (var exchangeProgram in exchangeProgramsList)
            {
                exchangeProgramsResponse.Add(new GetExchangeProgramResponse(exchangeProgram.Id.Value,
                                                              exchangeProgram.Name.Value,
                                                              exchangeProgram.Description.Value,
                                                              exchangeProgram.LimitApplicationDate.Value,
                                                              exchangeProgram.StartDate.Value,
                                                              exchangeProgram.FinishDate.Value,
                                                              exchangeProgram.ApplicationDocuments.Value,
                                                              exchangeProgram.RequiredDocuments.Value,
                                                              exchangeProgram.Images.Value,
                                                              exchangeProgram.OrganizationId,
                                                              exchangeProgram.CountryId,
                                                              exchangeProgram.StateId,
                                                              exchangeProgram.StatusId));
            }

            return TypedResults.Ok(exchangeProgramsResponse);
        }
        catch (Exception e)
        {
            return TypedResults.BadRequest(e.Message);
        }
    }

    public static async Task<Results<Ok, UnprocessableEntity<string>, BadRequest<string>>> Update(UpdateExchangeProgramRequest request,
        ISender sender)
    {
        try
        {
            var command = new UpdateExchangeProgramCommand(
                request.Id,
                request.Name,
                request.Description,
                request.LimitApplicationDate,
                request.StartDate,
                request.FinishDate,
                request.ApplicationDocuments,
                request.RequiredDocuments,
                request.ImagesUrl,
                request.CountryId,
                request.StateId,
                request.StatusId);

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

    public static async Task<Results<Ok, UnprocessableEntity<string>, BadRequest<string>>> Close(int exchangeProgramId, ISender sender)
    {
        try
        {
            var command = new CloseExchangeProgramCommand(exchangeProgramId);

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
}
