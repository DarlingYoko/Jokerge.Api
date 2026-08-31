using FluentValidation;
using Gml.Dto.Integration;

namespace Gml.Web.Api.Core.Validation;

public class DiscordRpcValidator : AbstractValidator<DiscordRpcUpdateDto>
{
    public DiscordRpcValidator()
    {
        RuleFor(x => x.ClientId)
            .MaximumLength(32).WithMessage("ClientId должен содержать не более 32 символов");

        RuleFor(x => x.Details)
            .MaximumLength(128).WithMessage("Details не может быть длинее 128 символов");

        RuleFor(x => x.LargeImageKey)
            .MaximumLength(32).WithMessage("LargeImageKey не может быть длинее 32 символов");

        RuleFor(x => x.LargeImageText)
            .MaximumLength(128).WithMessage("LargeImageText не может быть длинее 128 символов");

        RuleFor(x => x.SmallImageKey)
            .MaximumLength(32).WithMessage("SmallImageKey не может быть длинее 32 символов");

        RuleFor(x => x.SmallImageText)
            .MaximumLength(128).WithMessage("SmallImageText не может быть длинее 128 символов");
    }
}
