[CmdletBinding(PositionalBinding = $false)]
param(
    [string]$DeviceId,
    [ValidateRange(1024, 65535)]
    [int]$LocalPort = 8796
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName System.Net.Http
[System.Windows.Forms.Application]::EnableVisualStyles()

# No credential command-line parameter, file, environment variable or transcript.
# ADB's default TCP forwarding listens on localhost; never enable its -a option.
$projectRoot = Split-Path -Parent $PSScriptRoot
$adbPath = Join-Path $projectRoot 'Tools\platform-tools\adb.exe'
$selectedDevice = $null
$ownsForward = $false
$httpClient = $null
$httpHandler = $null
$form = $null
$keyBox = $null
$pairingBox = $null
$textModelBox = $null
$visionModelBox = $null
$cloudConsentBox = $null
$visualConsentBox = $null
$toolExitCode = 1
$failureMessage = '无法启动 USB 配置工具。请确认 Windows 桌面环境和 ADB 可用。'
$localEndpoint = 'http://127.0.0.1:' + $LocalPort

function Invoke-LocalAdb {
    param([string[]]$Arguments)
    $nativeOutput = & $adbPath @Arguments 2>&1
    $nativeCode = $LASTEXITCODE
    return [pscustomobject]@{ Code = $nativeCode; Output = @($nativeOutput) }
}

function Get-LocalForward {
    $listing = Invoke-LocalAdb -Arguments @('forward', '--list')
    if ($listing.Code -ne 0) { throw 'ForwardListingFailed' }
    foreach ($entry in $listing.Output) {
        if ([string]$entry -match '^\s*(\S+)\s+(tcp:\d+)\s+(\S+)\s*$') {
            if ($Matches[2] -eq ('tcp:' + $LocalPort)) {
                return [pscustomobject]@{ Device = $Matches[1]; Remote = $Matches[3] }
            }
        }
    }
    return $null
}

function Test-ConfigurationService {
    $healthRequest = $null
    $healthResponse = $null
    try {
        $healthRequest = [System.Net.Http.HttpRequestMessage]::new([System.Net.Http.HttpMethod]::Get, $localEndpoint + '/health')
        # Check response headers only; do not read, display or log conversation state.
        $healthResponse = $httpClient.SendAsync($healthRequest, [System.Net.Http.HttpCompletionOption]::ResponseHeadersRead).GetAwaiter().GetResult()
        return $healthResponse.IsSuccessStatusCode -and $healthResponse.Content.Headers.ContentType.MediaType -eq 'application/json'
    } catch {
        return $false
    } finally {
        if ($null -ne $healthResponse) { $healthResponse.Dispose() }
        if ($null -ne $healthRequest) { $healthRequest.Dispose() }
    }
}

function Show-FixedFailure {
    param([string]$Message)
    [void][System.Windows.Forms.MessageBox]::Show($Message, 'Between Us · USB 配置', [System.Windows.Forms.MessageBoxButtons]::OK, [System.Windows.Forms.MessageBoxIcon]::Warning)
}

try {
    if (-not (Test-Path -LiteralPath $adbPath -PathType Leaf)) {
        $adbCommand = Get-Command adb.exe -CommandType Application -ErrorAction SilentlyContinue
        if ($null -eq $adbCommand) { $adbCommand = Get-Command adb -CommandType Application -ErrorAction SilentlyContinue }
        if ($null -eq $adbCommand) {
            $failureMessage = '找不到 ADB。请保留项目 Tools/platform-tools/adb.exe，或将 ADB 加入 PATH。'
            throw 'AdbMissing'
        }
        $adbPath = $adbCommand.Source
    }

    $failureMessage = '无法读取设备列表。请检查 USB 连接和 ADB。'
    $deviceListing = Invoke-LocalAdb -Arguments @('devices')
    if ($deviceListing.Code -ne 0) { throw 'DeviceListingFailed' }
    $authorizedDevices = @(
        foreach ($deviceLine in $deviceListing.Output) {
            if ([string]$deviceLine -match '^\s*(\S+)\s+device(?:\s|$)') { $Matches[1] }
        }
    )
    if ([string]::IsNullOrWhiteSpace($DeviceId)) {
        if ($authorizedDevices.Count -eq 0) {
            $failureMessage = '未找到已授权的设备。请连接并唤醒头显，在头显中允许 USB 调试后重试。'
            throw 'NoAuthorizedDevice'
        }
        if ($authorizedDevices.Count -ne 1) {
            $failureMessage = '连接了多个已授权设备。请关闭此工具，使用 -DeviceId 指定目标头显。'
            throw 'MultipleAuthorizedDevices'
        }
        $selectedDevice = $authorizedDevices[0]
    } else {
        $selectedDevice = $DeviceId.Trim()
        if ($authorizedDevices -cnotcontains $selectedDevice) {
            $failureMessage = '指定设备未连接或尚未授权。请检查 -DeviceId，并在头显中允许 USB 调试。'
            throw 'SelectedDeviceUnavailable'
        }
    }

    $failureMessage = '本地端口无法使用。请关闭占用端口的工具，或用 -LocalPort 指定其他本地端口。'
    $existingForward = Get-LocalForward
    if ($null -ne $existingForward) {
        if ($existingForward.Device -cne $selectedDevice -or $existingForward.Remote -ne 'tcp:8796') { throw 'ForwardAlreadyUsed' }
    } else {
        $forwardResult = Invoke-LocalAdb -Arguments @('-s', $selectedDevice, 'forward', '--no-rebind', ('tcp:' + $LocalPort), 'tcp:8796')
        if ($forwardResult.Code -ne 0) { throw 'ForwardFailed' }
        $ownsForward = $true
    }

    $httpHandler = [System.Net.Http.HttpClientHandler]::new()
    $httpHandler.UseProxy = $false
    $httpHandler.AllowAutoRedirect = $false
    $httpClient = [System.Net.Http.HttpClient]::new($httpHandler)
    $httpClient.Timeout = [TimeSpan]::FromSeconds(4)
    $failureMessage = '头显配置服务尚未就绪。请安装当前版本，在头显中打开 Between Us 后重试。'
    $serviceReady = Test-ConfigurationService
    if (-not $serviceReady) {
        # Starting the existing application preserves any running process/session.
        # Never force-stop, reinstall or call an upstream cloud API from this tool.
        $launchResult = Invoke-LocalAdb -Arguments @('-s', $selectedDevice, 'shell', 'am', 'start', '-n', 'com.prototype.pico.conversation/com.unity3d.player.UnityPlayerActivity')
        if ($launchResult.Code -eq 0) {
            for ($readyAttempt = 0; $readyAttempt -lt 5 -and -not $serviceReady; $readyAttempt++) {
                Start-Sleep -Milliseconds 400
                $serviceReady = Test-ConfigurationService
            }
        }
    }
    if (-not $serviceReady) { throw 'ConfigurationServiceUnavailable' }

    $form = [System.Windows.Forms.Form]::new()
    $form.Text = 'Between Us · USB 临时配置'
    $form.ClientSize = [System.Drawing.Size]::new(680, 620)
    $form.StartPosition = [System.Windows.Forms.FormStartPosition]::CenterScreen
    $form.FormBorderStyle = [System.Windows.Forms.FormBorderStyle]::FixedDialog
    $form.MaximizeBox = $false
    $form.Font = [System.Drawing.Font]::new('Microsoft YaHei UI', 10)

    function Add-FormLabel {
        param([string]$Text, [int]$X, [int]$Y, [int]$Width, [int]$Height)
        $label = [System.Windows.Forms.Label]::new()
        $label.Text = $Text
        $label.Location = [System.Drawing.Point]::new($X, $Y)
        $label.Size = [System.Drawing.Size]::new($Width, $Height)
        [void]$form.Controls.Add($label)
        return $label
    }

    [void](Add-FormLabel '在头显本次运行中配置 API Key' 24 18 632 30)
    [void](Add-FormLabel 'Key 仅通过本机 USB 转发交给头显；工具不保存。关闭头显应用后需要重新输入。' 24 51 632 48)
    [void](Add-FormLabel '头显配对码（在头显 API 设置中查看）' 24 108 632 25)
    $pairingBox = [System.Windows.Forms.TextBox]::new()
    $pairingBox.Location = [System.Drawing.Point]::new(24, 137)
    $pairingBox.Size = [System.Drawing.Size]::new(190, 28)
    $pairingBox.MaxLength = 6
    $pairingBox.TabIndex = 0
    [void]$form.Controls.Add($pairingBox)

    [void](Add-FormLabel '完整 API Key（ID.Secret，输入隐藏）' 24 180 632 25)
    $keyBox = [System.Windows.Forms.TextBox]::new()
    $keyBox.Location = [System.Drawing.Point]::new(24, 209)
    $keyBox.Size = [System.Drawing.Size]::new(632, 28)
    $keyBox.MaxLength = 4096
    $keyBox.UseSystemPasswordChar = $true
    $keyBox.TabIndex = 1
    [void]$form.Controls.Add($keyBox)

    [void](Add-FormLabel '文本模型（可选；留空使用默认值）' 24 252 304 25)
    [void](Add-FormLabel '视觉模型（可选；留空使用默认值）' 352 252 304 25)
    $textModelBox = [System.Windows.Forms.TextBox]::new()
    $textModelBox.Location = [System.Drawing.Point]::new(24, 281)
    $textModelBox.Size = [System.Drawing.Size]::new(304, 28)
    $textModelBox.MaxLength = 128
    $textModelBox.Text = 'glm-4.6'
    $textModelBox.TabIndex = 2
    [void]$form.Controls.Add($textModelBox)
    $visionModelBox = [System.Windows.Forms.TextBox]::new()
    $visionModelBox.Location = [System.Drawing.Point]::new(352, 281)
    $visionModelBox.Size = [System.Drawing.Size]::new(304, 28)
    $visionModelBox.MaxLength = 128
    $visionModelBox.Text = 'glm-4.6v-flashx'
    $visionModelBox.TabIndex = 3
    [void]$form.Controls.Add($visionModelBox)

    $cloudConsentBox = [System.Windows.Forms.CheckBox]::new()
    $cloudConsentBox.Text = '我已取得参与者同意，允许将本次语音和文本提交至智谱 BigModel 云端处理，并使用所填 Key 的额度。'
    $cloudConsentBox.Location = [System.Drawing.Point]::new(24, 330)
    $cloudConsentBox.Size = [System.Drawing.Size]::new(632, 62)
    $cloudConsentBox.Checked = $false
    $cloudConsentBox.TabIndex = 4
    [void]$form.Controls.Add($cloudConsentBox)
    $visualConsentBox = [System.Windows.Forms.CheckBox]::new()
    $visualConsentBox.Text = '同时允许上传本句的人脸照片进行视觉分析（可选）'
    $visualConsentBox.Location = [System.Drawing.Point]::new(24, 398)
    $visualConsentBox.Size = [System.Drawing.Size]::new(632, 40)
    $visualConsentBox.Checked = $false
    $visualConsentBox.TabIndex = 5
    [void]$form.Controls.Add($visualConsentBox)

    $statusLabel = Add-FormLabel '请先结束头显中正在进行的交流，再提交配置。' 24 452 632 78
    $statusLabel.ForeColor = [System.Drawing.Color]::FromArgb(75, 85, 99)
    $submitButton = [System.Windows.Forms.Button]::new()
    $submitButton.Text = '配置本次运行'
    $submitButton.Location = [System.Drawing.Point]::new(358, 552)
    $submitButton.Size = [System.Drawing.Size]::new(160, 40)
    $submitButton.Enabled = $false
    $submitButton.TabIndex = 6
    [void]$form.Controls.Add($submitButton)
    $closeButton = [System.Windows.Forms.Button]::new()
    $closeButton.Text = '关闭'
    $closeButton.Location = [System.Drawing.Point]::new(536, 552)
    $closeButton.Size = [System.Drawing.Size]::new(120, 40)
    $closeButton.TabIndex = 7
    $closeButton.DialogResult = [System.Windows.Forms.DialogResult]::Cancel
    [void]$form.Controls.Add($closeButton)
    $form.AcceptButton = $submitButton
    $form.CancelButton = $closeButton

    $cloudConsentBox.Add_CheckedChanged({ $submitButton.Enabled = $cloudConsentBox.Checked })
    $submitButton.Add_Click({
        if (-not $cloudConsentBox.Checked) {
            $statusLabel.Text = '请先确认参与者已同意语音和文本的云端处理。'
            return
        }
        $submittedKey = $null
        $configurationData = $null
        $configurationJson = $null
        $configurationBytes = $null
        $configurationRequest = $null
        $configurationResponse = $null
        try {
            $pairingCode = $pairingBox.Text.Trim()
            if ($pairingCode -notmatch '\A[0-9]{6}\z') {
                $statusLabel.Text = '请输入头显当前显示的六位配对码。'
                return
            }
            $submittedKey = $keyBox.Text.Trim()
            if ($submittedKey -notmatch '\A[A-Za-z0-9_+/=-]+\.[A-Za-z0-9_+/=-]+\z') {
                $statusLabel.Text = '请输入完整 API Key（ID.Secret），不要输入掩码或 Bearer 前缀。'
                return
            }
            $textModel = $textModelBox.Text.Trim()
            $visionModel = $visionModelBox.Text.Trim()
            foreach ($modelName in @($textModel, $visionModel)) {
                if ($modelName.Length -gt 0 -and $modelName -notmatch '\A[A-Za-z0-9][A-Za-z0-9._:/-]{0,127}\z') {
                    $statusLabel.Text = '模型名称格式不正确；也可留空使用默认值。'
                    return
                }
            }
            $submitButton.Enabled = $false
            $statusLabel.Text = '正在通过 USB 配置头显……'
            $form.Refresh()
            $configurationData = [ordered]@{
                pairing_code = $pairingCode
                key = $submittedKey
                text_model = $textModel
                vision_model = $visionModel
                consent = $visualConsentBox.Checked
            }
            $configurationJson = $configurationData | ConvertTo-Json -Compress
            $configurationBytes = [System.Text.Encoding]::UTF8.GetBytes($configurationJson)
            $configurationRequest = [System.Net.Http.HttpRequestMessage]::new([System.Net.Http.HttpMethod]::Post, $localEndpoint + '/config')
            $configurationRequest.Content = [System.Net.Http.ByteArrayContent]::new($configurationBytes)
            $configurationRequest.Content.Headers.ContentType = [System.Net.Http.Headers.MediaTypeHeaderValue]::new('application/json')
            $configurationResponse = $httpClient.SendAsync($configurationRequest, [System.Net.Http.HttpCompletionOption]::ResponseHeadersRead).GetAwaiter().GetResult()
            # Never display any response body or exception details.
            if ($configurationResponse.IsSuccessStatusCode) {
                $keyBox.Clear()
                $statusLabel.Text = '配置成功，Key 输入已清空。此配置仅用于头显本次运行；可关闭工具并拔掉 USB，再在头显中按 X 开始。'
            } elseif ([int]$configurationResponse.StatusCode -eq 403) {
                $statusLabel.Text = '配对未通过。请查看头显 API 设置中的当前配对码后重试。'
            } else {
                $statusLabel.Text = '配置未完成。请先结束当前交流，确认配对码和输入内容后重试。'
            }
        } catch {
            $statusLabel.Text = '配置请求失败。请检查 USB 连接，并确认当前版本应用正在头显中运行。'
        } finally {
            if ($null -ne $configurationResponse) { $configurationResponse.Dispose() }
            if ($null -ne $configurationRequest) { $configurationRequest.Dispose() }
            if ($null -ne $configurationBytes) { [Array]::Clear($configurationBytes, 0, $configurationBytes.Length) }
            if ($null -ne $configurationData) { $configurationData['key'] = $null }
            $submittedKey = $null
            $configurationData = $null
            $configurationJson = $null
            $configurationBytes = $null
            $submitButton.Enabled = $cloudConsentBox.Checked
        }
    })
    $form.Add_FormClosing({
        $keyBox.Clear()
        $pairingBox.Clear()
        $textModelBox.Clear()
        $visionModelBox.Clear()
        $cloudConsentBox.Checked = $false
        $visualConsentBox.Checked = $false
    })
    [void]$form.ShowDialog()
    $toolExitCode = 0
} catch {
    Show-FixedFailure -Message $failureMessage
} finally {
    if ($null -ne $keyBox) { $keyBox.Clear() }
    if ($null -ne $form) { $form.Dispose() }
    if ($null -ne $httpClient) { $httpClient.Dispose() }
    elseif ($null -ne $httpHandler) { $httpHandler.Dispose() }
    if ($ownsForward -and $null -ne $selectedDevice) {
        try {
            $currentForward = Get-LocalForward
            if ($null -ne $currentForward -and $currentForward.Device -ceq $selectedDevice -and $currentForward.Remote -eq 'tcp:8796') {
                [void](Invoke-LocalAdb -Arguments @('-s', $selectedDevice, 'forward', '--remove', ('tcp:' + $LocalPort)))
            }
        } catch {
            # Cleanup never prints command output or exception details.
        }
    }
}
exit $toolExitCode
