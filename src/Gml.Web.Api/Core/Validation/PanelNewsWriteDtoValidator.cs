using FluentValidation;
using Gml.Dto.News;

namespace Gml.Web.Api.Core.Validation;

public class PanelNewsWriteDtoValidator : AbstractValidator<PanelNewsWriteDto>
{
    public const int TitleMaxLength = 200;
    public const int ContentMaxLength = 20000;
    public const int BindingMaxLength = 100;

    public PanelNewsWriteDtoValidator()
    {
        RuleFor(x => x.Title)
            .Must(title => !string.IsNullOrWhiteSpace(title)).WithMessage("Заголовок новости обязателен.")
            .MaximumLength(TitleMaxLength)
            .WithMessage($"Заголовок не должен быть длиннее {TitleMaxLength} символов.");

        RuleFor(x => x.Content)
            .NotNull().WithMessage("Текст новости обязателен.")
            .MaximumLength(ContentMaxLength)
            .WithMessage($"Текст не должен быть длиннее {ContentMaxLength} символов.");

        RuleFor(x => x.ProfileName)
            .MaximumLength(BindingMaxLength)
            .WithMessage($"Имя профиля не должно быть длиннее {BindingMaxLength} символов.");

        RuleFor(x => x.BuildVersion)
            .MaximumLength(BindingMaxLength)
            .WithMessage($"Версия сборки не должна быть длиннее {BindingMaxLength} символов.");
    }
}
