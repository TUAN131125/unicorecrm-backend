param([Parameter(Mandatory)][string]$BaseUrl, [Parameter(Mandatory)][string]$WorkspaceId)
$ErrorActionPreference = 'Stop'
if (-not $env:UNICORE_PRODUCT_SEARCH_TEST_TOKEN) { throw 'Synthetic fixture token is required.' }
$headers = @{ Authorization = "Bearer $env:UNICORE_PRODUCT_SEARCH_TEST_TOKEN"; 'X-Workspace-Id' = $WorkspaceId; 'X-Request-Id' = [guid]::NewGuid().ToString(); 'X-Correlation-Id' = [guid]::NewGuid().ToString() }
$page = Invoke-RestMethod -Uri "$BaseUrl/products/selection?limit=2" -Headers $headers
if ($page.items.Count -gt 2) { throw 'Page bound was exceeded.' }
if ($page.PSObject.Properties.Name -contains 'totalCount') { throw 'Selection read exposed a catalog count.' }
foreach ($item in $page.items) {
    if ($item.status -ne 'ACTIVE') { throw 'Search returned a non-active Product.' }
    foreach ($field in @('unitPrice', 'taxRate', 'discount', 'billingCycle')) {
        if ($item.PSObject.Properties.Name -contains $field) { throw 'Selection projection exposed commercial fields.' }
    }
}
foreach ($query in @('limit=51', 'limit=0', 'limit=-1', 'limit=abc', 'cursor=invalid', 'ids=a&ids=a', 'ids=a&limit=2')) {
    $response = Invoke-WebRequest -Uri "$BaseUrl/products/selection?$query" -Headers $headers -SkipHttpErrorCheck
    if ($response.StatusCode -ne 400) { throw "Expected bounded query validation refusal for $query." }
}
Write-Output 'Products selection HTTP bounds/projection focused checks: PASS'
