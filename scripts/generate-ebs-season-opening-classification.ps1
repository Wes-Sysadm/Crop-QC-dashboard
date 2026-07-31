[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$evidencePath = Join-Path $repositoryRoot 'docs/ebs-2026-season-opening-production-evidence.csv'
$outputPath = Join-Path $repositoryRoot 'docs/ebs-2026-season-opening-classification.csv'

$rows = @(Import-Csv -LiteralPath $evidencePath)
if ($rows.Count -ne 79) {
    throw "Expected 79 EBS rows outside Evans 7; found $($rows.Count)."
}

$classified = foreach ($row in $rows) {
    $id = [int]$row.adjustment_id
    $classification = switch ($id) {
        1 {
            @{
                Number = 1
                Name = 'Valid prior-season historical activity with a carried balance requiring a season-opening zero'
                Treatment = 'Set the carried ledger impact to zero while preserving receipt 26 and its history.'
                Target = 'yes'
            }
            break
        }
        8 {
            @{
                Number = 3
                Name = 'Invalid test or duplicate data safe to remove'
                Treatment = 'Set the duplicate ReceiptAdd impact to zero; preserve soft-deleted receipt 28 and Bins Run history.'
                Target = 'yes'
            }
            break
        }
        { $_ -in 22, 23, 25, 26 } {
            @{
                Number = 4
                Name = 'Invalid negative-balance data requiring direct cleanup'
                Treatment = 'Restore the missing positive source quantity so the existing linked Bins Run deduction nets the lot to zero.'
                Target = 'yes'
            }
            break
        }
        { $_ -in 76, 77, 78, 79 } {
            @{
                Number = 4
                Name = 'Invalid negative-balance data requiring direct cleanup'
                Treatment = 'Preserve the valid linked Bins Run deduction unchanged.'
                Target = 'no'
            }
            break
        }
        default {
            @{
                Number = 2
                Name = 'Valid prior-season history already netting to zero and requiring no change'
                Treatment = 'Preserve unchanged.'
                Target = 'no'
            }
        }
    }

    [pscustomobject][ordered]@{
        category_number = $classification.Number
        category = $classification.Name
        recommended_treatment = $classification.Treatment
        correction_target = $classification.Target
        adjustment_id = $row.adjustment_id
        room_id = $row.room_id
        room = $row.room
        receipt_id = $row.receipt_id
        receipt_number = $row.receipt_number
        receipt_received_pacific = $row.receipt_received_pacific
        receipt_crop_year = $row.receipt_crop_year
        receipt_is_test = $row.receipt_is_test
        receipt_is_deleted = $row.receipt_is_deleted
        grower = $row.grower
        grower_lot = $row.grower_lot
        variety = $row.variety
        organic = $row.IsOrganic
        adjustment_crop_year = $row.adjustment_crop_year
        quantity = $row.quantity
        old_bin_count = $row.OldBinCount
        new_bin_count = $row.NewBinCount
        transaction_type = $row.transaction_type
        source = $row.Source
        reason = $row.Reason
        bins_run_links = $row.bins_run_links
        room_depletion_id = $row.RoomDepletionId
        depletion_receipt_id = $row.depletion_receipt_id
        bins_depleted = $row.BinCountDepleted
        room_transfer_id = $row.RoomTransferId
        actual_run_id = $row.ActualRunId
        actual_run_revision_id = $row.ActualRunRevisionId
        created_at = $row.CreatedAt
        adjustment_at = $row.AdjustmentAt
        adjustment_pacific = $row.adjustment_pacific
        boundary_receipt_id = $row.boundary_receipt_id
        boundary_pacific = $row.boundary_pacific
        before_boundary = $row.before_boundary
    }
}

$ids = @($classified | ForEach-Object { [int]$_.adjustment_id })
if (($ids | Sort-Object -Unique).Count -ne 79) {
    throw 'The production evidence contains duplicate adjustment IDs.'
}
if (@($classified | Where-Object boundary_receipt_id -ne '99').Count -ne 0) {
    throw 'The evidence does not consistently identify boundary receipt 99.'
}
if (@($classified | Where-Object before_boundary -ne 't').Count -ne 0) {
    throw 'At least one candidate row is not before the verified season boundary.'
}

$expectedCounts = @{ 1 = 1; 2 = 69; 3 = 1; 4 = 8 }
foreach ($categoryNumber in $expectedCounts.Keys) {
    $actual = @($classified | Where-Object category_number -eq $categoryNumber).Count
    if ($actual -ne $expectedCounts[$categoryNumber]) {
        throw "Category $categoryNumber expected $($expectedCounts[$categoryNumber]) rows; found $actual."
    }
}

$csv = $classified | Sort-Object { [int]$_.adjustment_id } | ConvertTo-Csv -NoTypeInformation
[System.IO.File]::WriteAllLines($outputPath, $csv, [System.Text.UTF8Encoding]::new($false))
Write-Host "Wrote $($classified.Count) classified rows to $outputPath"
