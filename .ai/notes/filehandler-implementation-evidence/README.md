# Evidence remediation — 2026-09-18 [CDX]

- `observations.json`: 24 runtime observations, chạy lại từ probe review trên mã đã sửa.
- `extra-observations.json`: 4 supplemental observations (MIME XML, bound SDT, SST scope).
- `http-observations.json`: 4 request Kestrel ngày 2026-09-17; chunked quota/output quota/invalid relationship.
- `documentation-audit.json`: 108 files, 918 declarations, 0 findings.
- `test-results/implementation.trx`: 359 passed, 0 failed/skipped.

Probe cũ `office-anchor-order` dùng một alternativeOrder hardcode nay trùng thứ tự nguồn đúng, nên accepted=true không còn biểu thị bypass. Regression test mới kiểm đảo thứ tự và phải reject. `edit-mask-ignored` vẫn có structureAccepted=true ở validator đếm cấu trúc; packageAccepted=false cho thấy lớp XML invariant bắt buộc đã chặn thay đổi style.

Từ repository root:

```powershell
dotnet test filehandler/FileHandler.sln -c Release --no-restore --artifacts-path .ai/notes/filehandler-review-evidence/artifacts -p:GenerateDocumentationFile=true
$env:ArtifactsPath = 'D:\Projects\trans_sys\.ai\notes\filehandler-review-evidence\artifacts'
$env:UseArtifactsOutput = 'true'
dotnet format filehandler/FileHandler.sln --verify-no-changes --no-restore
```

Chạy probe từ thư mục evidence này để không ghi đè baseline review:

```powershell
$env:DOTNET_TieredCompilation = '0'
dotnet run --project ../filehandler-review-evidence/ReviewProbe.csproj -c Release --artifacts-path ../filehandler-review-evidence/artifacts/probe -p:NuGetAudit=false
dotnet ../filehandler-review-evidence/artifacts/probe/bin/ReviewProbe/release/FileHandler.Tests.dll extra
dotnet ../filehandler-review-evidence/artifacts/probe/bin/ReviewProbe/release/FileHandler.Tests.dll audit
```

HTTP script cần host local với cùng limits mô tả trong báo cáo và source fixture ở artifacts. Host kiểm thử đã được dừng. Artifacts/probe stdout/trace bị Git ignore; JSON observations chỉ chứa fixture tổng hợp. Không chứa tài liệu người dùng.
