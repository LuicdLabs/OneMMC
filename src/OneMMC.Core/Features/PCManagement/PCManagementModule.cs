using OneMMC.Core.Features.PCManagement.Services.DevMgmt;
using OneMMC.Core.Features.PCManagement.Services.DiskMgmt;
using OneMMC.Core.Features.PCManagement.Services.EventViewer;
using OneMMC.Core.Features.PCManagement.Services.FsMgmt;
using OneMMC.Core.Features.PCManagement.Services.LusrMgr;
using OneMMC.Core.Features.PCManagement.Services.PerfMon;
using OneMMC.Core.Features.PCManagement.Services.TaskSchd;
using OneMMC.Core.Features.PCManagement.Services.WindowsServices;
using OneMMC.Core.Features.PCManagement.ViewModels.DiskMgmt;
using OneMMC.Core.Features.PCManagement.ViewModels.DevMgmt;
using OneMMC.Core.Features.PCManagement.ViewModels.EventViewer;
using OneMMC.Core.Features.PCManagement.ViewModels.FsMgmt;
using OneMMC.Core.Features.PCManagement.ViewModels.LusrMgr;
using OneMMC.Core.Features.PCManagement.ViewModels.PerfMon;
using OneMMC.Core.Features.PCManagement.ViewModels.Services;
using OneMMC.Core.Features.PCManagement.ViewModels.TaskSchd;
using Microsoft.Extensions.DependencyInjection;

namespace OneMMC.Core.Features.PCManagement;

internal static class PCManagementModule
{
    internal static IServiceCollection AddPCManagement(this IServiceCollection services)
    {
        services.AddTransient<DeviceManagerService>();
        services.AddTransient<DiskManagementService>();
        services.AddTransient<EventViewerService>();
        services.AddTransient<SharedFoldersService>();
        services.AddTransient<LocalUserGroupManager>();
        services.AddTransient<PerformanceMonitorService>();
        services.AddTransient<WindowsServiceManager>();

        // Task Scheduler service holds a dedicated STA COM thread + cached connection, so it is a singleton.
        services.AddSingleton<ITaskSchedulerService, TaskSchedulerService>();
        services.AddTransient<TaskHistoryService>();

        services.AddTransient<DeviceManagerViewModel>();
        services.AddTransient<DiskManagementViewModel>();
        services.AddTransient<EventViewerViewModel>();
        services.AddTransient<SharedFoldersViewModel>();
        services.AddTransient<LocalUsersGroupsViewModel>();
        services.AddTransient<PerformanceMonitorViewModel>();
        services.AddTransient<ServicesViewModel>();
        services.AddTransient<TaskSchedulerViewModel>();

        return services;
    }
}
