using Microsoft.Win32.SafeHandles;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace GeniaProxy.Services
{
    public sealed class WindowsJobObject : IDisposable
    {
        private const uint KillOnJobClose = 0x00002000;

        private readonly SafeFileHandle handle;
        private bool disposed;

        private WindowsJobObject(
            SafeFileHandle handle)
        {
            this.handle = handle;
        }

        public static WindowsJobObject
            CreateKillOnClose()
        {
            SafeFileHandle handle =
                CreateJobObject(
                    IntPtr.Zero,
                    null
                );

            if (handle.IsInvalid)
            {
                throw new Win32Exception(
                    Marshal.GetLastWin32Error(),
                    "Не удалось создать объект задания Windows."
                );
            }

            var information =
                new JobObjectExtendedLimitInformation
                {
                    BasicLimitInformation =
                        new JobObjectBasicLimitInformation
                        {
                            LimitFlags = KillOnJobClose
                        }
                };

            int size = Marshal.SizeOf<
                JobObjectExtendedLimitInformation>();

            if (!SetInformationJobObject(
                    handle,
                    JobObjectInfoType
                        .ExtendedLimitInformation,
                    ref information,
                    (uint)size))
            {
                int error = Marshal.GetLastWin32Error();
                handle.Dispose();

                throw new Win32Exception(
                    error,
                    "Не удалось настроить объект задания Windows."
                );
            }

            return new WindowsJobObject(handle);
        }

        public void Assign(Process process)
        {
            ObjectDisposedException.ThrowIf(
                disposed,
                this
            );

            ArgumentNullException.ThrowIfNull(process);

            if (!AssignProcessToJobObject(
                    handle,
                    process.Handle))
            {
                throw new Win32Exception(
                    Marshal.GetLastWin32Error(),
                    "Не удалось привязать sing-box " +
                    "к объекту задания Windows."
                );
            }
        }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            handle.Dispose();
        }

        private enum JobObjectInfoType
        {
            ExtendedLimitInformation = 9
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct JobObjectBasicLimitInformation
        {
            public long PerProcessUserTimeLimit;
            public long PerJobUserTimeLimit;
            public uint LimitFlags;
            public UIntPtr MinimumWorkingSetSize;
            public UIntPtr MaximumWorkingSetSize;
            public uint ActiveProcessLimit;
            public UIntPtr Affinity;
            public uint PriorityClass;
            public uint SchedulingClass;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct IoCounters
        {
            public ulong ReadOperationCount;
            public ulong WriteOperationCount;
            public ulong OtherOperationCount;
            public ulong ReadTransferCount;
            public ulong WriteTransferCount;
            public ulong OtherTransferCount;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct JobObjectExtendedLimitInformation
        {
            public JobObjectBasicLimitInformation
                BasicLimitInformation;

            public IoCounters IoInfo;
            public UIntPtr ProcessMemoryLimit;
            public UIntPtr JobMemoryLimit;
            public UIntPtr PeakProcessMemoryUsed;
            public UIntPtr PeakJobMemoryUsed;
        }

        [DllImport(
            "kernel32.dll",
            CharSet = CharSet.Unicode,
            SetLastError = true)]
        private static extern SafeFileHandle
            CreateJobObject(
                IntPtr jobAttributes,
                string? name
            );

        [DllImport(
            "kernel32.dll",
            SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool
            SetInformationJobObject(
                SafeFileHandle job,
                JobObjectInfoType informationClass,
                ref JobObjectExtendedLimitInformation
                    information,
                uint informationLength
            );

        [DllImport(
            "kernel32.dll",
            SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool
            AssignProcessToJobObject(
                SafeFileHandle job,
                IntPtr process
            );
    }
}
