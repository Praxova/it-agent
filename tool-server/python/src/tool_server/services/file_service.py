"""File permission operations via WinRM/PowerShell."""

import logging
import fnmatch
from enum import Enum

import winrm

from ..config import Settings

logger = logging.getLogger(__name__)


class PermissionLevel(str, Enum):
    """Supported permission levels for file operations."""

    READ = "Read"
    WRITE = "Write"
    # Note: FullControl intentionally not supported


class FileServiceError(Exception):
    """Base exception for file service errors."""

    pass


class PathNotAllowedError(FileServiceError):
    """Path is in denied list or not in allowed list."""

    pass


class PathNotFoundError(FileServiceError):
    """Path does not exist."""

    pass


class UserNotFoundError(FileServiceError):
    """User not found."""

    pass


class FileService:
    """File permission operations via WinRM/PowerShell."""

    def __init__(self, settings: Settings):
        """Initialize file service.

        Args:
            settings: Configuration settings with WinRM parameters.
        """
        self.settings = settings
        self._session: winrm.Session | None = None

    def _get_session(self) -> winrm.Session:
        """Get or create WinRM session.

        Returns:
            WinRM session instance.
        """
        if self._session is None:
            # Use winrm credentials if provided, otherwise use ldap credentials
            username = self.settings.winrm_username or self.settings.ldap_bind_user
            password = self.settings.winrm_password or self.settings.ldap_bind_password

            # Format: user@domain or domain\user
            if '@' not in username and '\\' not in username:
                username = f"{username}@{self.settings.ldap_domain}"

            self._session = winrm.Session(
                target=f"http://{self.settings.winrm_host}:5985/wsman",
                auth=(username, password),
                transport=self.settings.winrm_transport,
            )
        return self._session

    def _run_powershell(self, script: str) -> tuple[str, str, int]:
        """Execute PowerShell script on remote host.

        Args:
            script: PowerShell script to execute.

        Returns:
            Tuple of (stdout, stderr, return_code).
        """
        session = self._get_session()
        result = session.run_ps(script)

        stdout = result.std_out.decode("utf-8", errors="replace").strip()
        stderr = result.std_err.decode("utf-8", errors="replace").strip()

        return stdout, stderr, result.status_code

    def health_check(self) -> bool:
        """Check if WinRM connection works.

        Returns:
            True if connection successful, False otherwise.
        """
        try:
            stdout, stderr, code = self._run_powershell("Write-Output 'OK'")
            return code == 0 and "OK" in stdout
        except Exception as e:
            logger.error(f"WinRM health check failed: {e}")
            return False

    def is_path_allowed(self, path: str) -> bool:
        """Check if path is allowed for modification.

        Rules:
        1. If path matches any denied pattern -> False
        2. If allowed_paths is empty -> True (permissive mode)
        3. If path matches any allowed pattern -> True
        4. Otherwise -> False

        Args:
            path: File system path to check.

        Returns:
            True if path is allowed, False otherwise.
        """
        # Normalize path separators
        normalized = path.replace("/", "\\")

        # Check denied paths
        for pattern in self.settings.file_permission_denied_paths:
            if fnmatch.fnmatch(normalized.lower(), pattern.lower()):
                logger.warning(f"Path {path} matches denied pattern {pattern}")
                return False

        # If no allowed paths specified, allow everything not denied
        if not self.settings.file_permission_allowed_paths:
            return True

        # Check allowed paths
        for pattern in self.settings.file_permission_allowed_paths:
            if fnmatch.fnmatch(normalized.lower(), pattern.lower()):
                return True

        logger.warning(f"Path {path} not in allowed paths list")
        return False

    def path_exists(self, path: str) -> bool:
        """Check if path exists on file server.

        Args:
            path: File system path to check.

        Returns:
            True if path exists, False otherwise.
        """
        script = f'Test-Path -Path "{path}"'
        stdout, stderr, code = self._run_powershell(script)
        return code == 0 and stdout.lower() == "true"

    def get_current_permissions(self, path: str, username: str) -> list[str]:
        """Get current permissions for a user on a path.

        Args:
            path: File system path.
            username: sAMAccountName.

        Returns:
            List of permission strings (e.g., ["ReadAndExecute", "Write"]).
        """
        script = f'''
$acl = Get-Acl -Path "{path}"
$rules = $acl.Access | Where-Object {{
    $_.IdentityReference -like "*\\{username}" -or
    $_.IdentityReference -eq "{username}"
}}
$rules | ForEach-Object {{ $_.FileSystemRights.ToString() }}
'''
        stdout, stderr, code = self._run_powershell(script)

        if code != 0:
            logger.error(f"Failed to get permissions: {stderr}")
            return []

        if not stdout:
            return []

        return [line.strip() for line in stdout.split("\n") if line.strip()]

    def grant_permission(
        self,
        path: str,
        username: str,
        permission: PermissionLevel,
    ) -> bool:
        """Grant permission to user on path.

        Args:
            path: UNC path (e.g., \\\\server\\share\\folder).
            username: sAMAccountName.
            permission: Read or Write.

        Returns:
            True if successful.

        Raises:
            PathNotAllowedError: Path is denied.
            PathNotFoundError: Path doesn't exist.
            FileServiceError: PowerShell operation failed.
        """
        # Validate path
        if not self.is_path_allowed(path):
            raise PathNotAllowedError(f"Path '{path}' is not allowed for modification")

        if not self.path_exists(path):
            raise PathNotFoundError(f"Path '{path}' does not exist")

        # Map permission level to NTFS rights
        rights_map = {
            PermissionLevel.READ: "ReadAndExecute",
            PermissionLevel.WRITE: "Modify",
        }
        rights = rights_map[permission]

        # Build domain\user format
        domain_part = self.settings.ldap_domain.split('.')[0]
        domain_user = f"{domain_part}\\{username}"

        script = f'''
$path = "{path}"
$user = "{domain_user}"
$rights = "{rights}"

try {{
    $acl = Get-Acl -Path $path
    $rule = New-Object System.Security.AccessControl.FileSystemAccessRule(
        $user,
        $rights,
        "ContainerInherit,ObjectInherit",
        "None",
        "Allow"
    )
    $acl.AddAccessRule($rule)
    Set-Acl -Path $path -AclObject $acl
    Write-Output "SUCCESS"
}} catch {{
    Write-Error $_.Exception.Message
    exit 1
}}
'''

        stdout, stderr, code = self._run_powershell(script)

        if code != 0 or "SUCCESS" not in stdout:
            error_msg = stderr or stdout or "Unknown error"
            logger.error(f"Failed to grant permission: {error_msg}")
            raise FileServiceError(f"Failed to grant permission: {error_msg}")

        logger.info(f"Granted {permission} to {username} on {path}")
        return True

    def revoke_permission(
        self,
        path: str,
        username: str,
    ) -> bool:
        """Revoke all explicit permissions for user on path.

        Args:
            path: UNC path.
            username: sAMAccountName.

        Returns:
            True if successful.

        Raises:
            PathNotAllowedError: Path is denied.
            PathNotFoundError: Path doesn't exist.
            FileServiceError: PowerShell operation failed.
        """
        if not self.is_path_allowed(path):
            raise PathNotAllowedError(f"Path '{path}' is not allowed for modification")

        if not self.path_exists(path):
            raise PathNotFoundError(f"Path '{path}' does not exist")

        domain_part = self.settings.ldap_domain.split('.')[0]
        domain_user = f"{domain_part}\\{username}"

        script = f'''
$path = "{path}"
$user = "{domain_user}"

try {{
    $acl = Get-Acl -Path $path
    $rules = $acl.Access | Where-Object {{
        $_.IdentityReference -like "*\\{username}" -or
        $_.IdentityReference -eq "{domain_user}"
    }}

    if ($rules) {{
        foreach ($rule in $rules) {{
            $acl.RemoveAccessRule($rule) | Out-Null
        }}
        Set-Acl -Path $path -AclObject $acl
    }}
    Write-Output "SUCCESS"
}} catch {{
    Write-Error $_.Exception.Message
    exit 1
}}
'''

        stdout, stderr, code = self._run_powershell(script)

        if code != 0 or "SUCCESS" not in stdout:
            error_msg = stderr or stdout or "Unknown error"
            logger.error(f"Failed to revoke permission: {error_msg}")
            raise FileServiceError(f"Failed to revoke permission: {error_msg}")

        logger.info(f"Revoked permissions for {username} on {path}")
        return True

    def list_permissions(self, path: str) -> list[dict]:
        """List all permissions on a path.

        Args:
            path: File system path.

        Returns:
            List of {user, rights, type} dicts.

        Raises:
            PathNotFoundError: Path doesn't exist.
            FileServiceError: PowerShell operation failed.
        """
        if not self.path_exists(path):
            raise PathNotFoundError(f"Path '{path}' does not exist")

        script = f'''
$acl = Get-Acl -Path "{path}"
$acl.Access | ForEach-Object {{
    "$($_.IdentityReference)|$($_.FileSystemRights)|$($_.AccessControlType)"
}}
'''
        stdout, stderr, code = self._run_powershell(script)

        if code != 0:
            raise FileServiceError(f"Failed to list permissions: {stderr}")

        permissions = []
        for line in stdout.split("\n"):
            if "|" in line:
                parts = line.strip().split("|")
                if len(parts) >= 3:
                    permissions.append({
                        "user": parts[0],
                        "rights": parts[1],
                        "type": parts[2],
                    })

        return permissions
