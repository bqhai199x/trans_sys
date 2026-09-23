# FileHandler

`FileHandler` là ASP.NET Core service chạy trên **.NET 10**, extract text từ file và apply mảng bản dịch vào file nguồn. Input là upload `multipart/form-data`; output là JSON chứa text/metadata hoặc `multipart/mixed` chứa metadata và file kết quả. Service không thực hiện dịch ngôn ngữ và không lưu phiên Import để dùng cho Export.

## Main operations

| Operation | Mục đích |
|---|---|
| Import | Extract `texts` theo thứ tự nguồn; trả metadata, skip và tùy chọn `units.json`. |
| Export | Đọc lại source file, validate `texts`, apply bản dịch và trả file cùng metadata. |
| Discovery | Liệt kê sheet Excel hoặc slide PowerPoint trước khi chọn nội dung. |

## Supported formats

| Format | Extension | Nội dung được xử lý |
|---|---|---|
| Markdown | `.md` | Heading, paragraph, nội dung inline hỗ trợ và label Mermaid được nhận diện. |
| Plain text | `.txt` | Paragraph phân cách bằng dòng trống/whitespace. |
| Word | `.docx` | Paragraph, table, textbox và các story được tham chiếu. |
| Excel | `.xlsx` | Tên worksheet, text cell và drawing paragraph được hỗ trợ. |
| PowerPoint | `.pptx` | Text paragraph trong shape/group và table trên slide được chọn. |

Extension được kiểm tra không phân biệt hoa/thường. Markdown/TXT đọc UTF-8; Office còn kiểm tra package thực tế. Chi tiết phạm vi xử lý nằm trong [processing.md](processing.md).

## Tài liệu

| Tài liệu | Nội dung |
|---|---|
| [API và data contracts](api.md) | 12 endpoint, request/response, Import/Export, metadata và example. |
| [File processing](processing.md) | Flow và rule theo 5 format. |
| [Token/placeholder](tokens.md) | Cú pháp `ox:rN`, `ox:kN`, `ox:bN`, bảo toàn và restore. |
| [Runtime](runtime.md) | Validation, error handling, configuration, logging, Swagger. |
| [Status và skip](skip-status-messages.md) | Ý nghĩa trạng thái, số đếm và các nhóm skip. |
| [Development và source navigation](development.md) | Project structure, build/run/test, class/method và đường dẫn source. |

## Quick start

Chạy từ `filehandler/`, dùng SDK **10.0.401** với `rollForward=latestPatch` theo [global.json](../global.json):

```powershell
dotnet restore FileHandler.sln
dotnet run --project src/FileHandler.Api
```

Launch profile `FileHandler.Api`: HTTP `http://localhost:57217`, HTTPS `https://localhost:57216`; Swagger UI tại [http://localhost:57217/swagger](http://localhost:57217/swagger). Xem [Build & Run](development.md#build--run) cho build và test.
