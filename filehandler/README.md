# FileHandler

REST API ASP.NET Core 10 xử lý Markdown theo luồng stateless: `POST /import` trả array chuỗi cần dịch; `POST /export` nhận lại đúng file nguồn và `translatedTexts` là JSON array nằm trong một field multipart, rồi trả file `.translated.md`.

## Chạy và kiểm thử

```powershell
dotnet restore FileHandler.sln
dotnet test FileHandler.sln
dotnet run --project src/FileHandler.Api
```

Swagger UI ở `/swagger`; OpenAPI JSON ở `/swagger/v1/swagger.json`.

```bash
curl -F "file=@guide.md" http://localhost:5000/import
curl -OJ -F "file=@guide.md" -F 'translatedTexts=["Bắt đầu nhanh"]' http://localhost:5000/export
```

Thành công import trả array JSON thuần. Thành công export trả `text/markdown; charset=utf-8` với attachment. Mọi lỗi trả array gồm `code`, `message` và các field định vị nếu có. HTTP 400 dùng cho multipart/JSON sai; 413 cho giới hạn tài nguyên; 415 cho extension khác `.md`; 422 cho UTF-8, count, marker hoặc mapping sai; 500 cho lỗi ngoài dự kiến. Reverse proxy có thể chặn request trước ứng dụng nên response của proxy không được ứng dụng chuẩn hóa.

## Giới hạn mặc định

- File nguồn: 5 MiB; multipart: 25 MiB; output: 20 MiB.
- 10.000 unit/tệp; 100.000 UTF-16 code unit/bản dịch.
- Chỉ UTF-8 nghiêm ngặt, có hoặc không BOM. BOM và bytes ngoài vùng dịch được giữ; identity export giữ nguyên toàn bộ bytes.
- Cấu hình trong `FileHandling` của `appsettings.json`.

API chỉ nhận file và `string[]`, không lưu phiên. Vì vậy server không thể chứng minh file export giống file import trước đó; caller phải gửi đúng nguồn. Các đoạn không có marker bị đảo thứ tự cũng không thể luôn được phát hiện. Marker kiểm tra tính toàn vẹn của từng unit, không chứng minh lịch sử import.

V1 giữ code, URL, autolink và HTML inline dưới marker bảo vệ; dịch heading/paragraph cùng emphasis và nhãn link. Code fence, front matter và thematic break không sinh unit. Soft break được biểu diễn bằng `\n`; khi bản dịch thay đổi, ký tự Markdown nhạy cảm được escape. Xem matrix và các khác biệt có chủ ý tại `docs/markdown-compatibility.md`.

Marker ID là số nguyên dương 32-bit ở dạng thập phân chuẩn, không có zero đầu. Dạng số quá lớn được coi là marker sai cú pháp và không gây overflow. Các tính năng còn ở trạng thái “Một phần” hoặc “Chưa hỗ trợ” trong matrix (đặc biệt unit phụ cho image/reference title và text/attribute HTML) không được xem là parity VS Code.
