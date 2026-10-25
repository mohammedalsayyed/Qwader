using FluentValidation;
using Qwader.Application.Auth;
using Qwader.Application.Common;
using Qwader.Domain.Identity;

namespace Qwader.Application.Users;

public sealed record RoleRefDto(string Name, string DisplayName);

public sealed record UserDto(
    int Id,
    string UserName,
    string FullName,
    string? PhoneNumber,
    bool IsActive,
    IReadOnlyList<RoleRefDto> Roles,
    DateTime CreatedAt,
    DateTime? LastLoginAt);

public sealed record CreateUserRequest(string UserName, string FullName, string? PhoneNumber, string Password, string[] Roles);

public sealed record UpdateUserRequest(string FullName, string? PhoneNumber, bool IsActive, string[] Roles);

public sealed record ResetPasswordRequest(string NewPassword);

public sealed record RoleDto(
    int Id,
    string Name,
    string DisplayName,
    string? Description,
    bool IsSystem,
    IReadOnlyList<string> Permissions,
    int UserCount);

public sealed record SaveRoleRequest(string DisplayName, string? Description, string[] Permissions);

public sealed record PermissionGroupDto(string Group, IReadOnlyList<PermissionInfo> Permissions);

public sealed class UserListRequest : PagedRequest
{
    public bool? IsActive { get; set; }
}

public interface IUserService
{
    Task<PagedResult<UserDto>> ListAsync(UserListRequest request, CancellationToken ct = default);
    Task<UserDto> GetAsync(int id, CancellationToken ct = default);
    Task<UserDto> CreateAsync(CreateUserRequest request, CancellationToken ct = default);
    Task<UserDto> UpdateAsync(int id, UpdateUserRequest request, CancellationToken ct = default);
    Task ResetPasswordAsync(int id, ResetPasswordRequest request, CancellationToken ct = default);
}

public interface IRoleService
{
    Task<IReadOnlyList<RoleDto>> ListAsync(CancellationToken ct = default);
    IReadOnlyList<PermissionGroupDto> PermissionCatalog();
    Task<RoleDto> CreateAsync(SaveRoleRequest request, CancellationToken ct = default);
    Task<RoleDto> UpdateAsync(int id, SaveRoleRequest request, CancellationToken ct = default);
    Task DeleteAsync(int id, CancellationToken ct = default);
}

public sealed class CreateUserRequestValidator : AbstractValidator<CreateUserRequest>
{
    public CreateUserRequestValidator()
    {
        RuleFor(x => x.UserName).NotEmpty().WithMessage("اسم المستخدم مطلوب")
            .Length(3, 50).WithMessage("اسم المستخدم يجب أن يكون بين 3 و50 حرفًا")
            .Matches(@"^\S+$").WithMessage("اسم المستخدم يجب ألا يحتوي على مسافات");
        RuleFor(x => x.FullName).NotEmpty().WithMessage("الاسم الكامل مطلوب").MaximumLength(100);
        RuleFor(x => x.PhoneNumber).MaximumLength(20).WithMessage("رقم الجوال طويل جدًا");
        RuleFor(x => x.Password).StrongPassword();
        RuleFor(x => x.Roles).NotEmpty().WithMessage("اختر دورًا واحدًا على الأقل");
    }
}

public sealed class UpdateUserRequestValidator : AbstractValidator<UpdateUserRequest>
{
    public UpdateUserRequestValidator()
    {
        RuleFor(x => x.FullName).NotEmpty().WithMessage("الاسم الكامل مطلوب").MaximumLength(100);
        RuleFor(x => x.PhoneNumber).MaximumLength(20).WithMessage("رقم الجوال طويل جدًا");
        RuleFor(x => x.Roles).NotEmpty().WithMessage("اختر دورًا واحدًا على الأقل");
    }
}

public sealed class ResetPasswordRequestValidator : AbstractValidator<ResetPasswordRequest>
{
    public ResetPasswordRequestValidator() => RuleFor(x => x.NewPassword).StrongPassword();
}

public sealed class SaveRoleRequestValidator : AbstractValidator<SaveRoleRequest>
{
    public SaveRoleRequestValidator()
    {
        RuleFor(x => x.DisplayName).NotEmpty().WithMessage("اسم الدور مطلوب").MaximumLength(50);
        RuleFor(x => x.Description).MaximumLength(200);
        RuleForEach(x => x.Permissions).Must(Permissions.Exists).WithMessage("صلاحية غير معروفة: {PropertyValue}");
    }
}
