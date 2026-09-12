# Không gian làm việc chia sẻ AI — Quy ước chung

Thư mục `.ai/` là không gian làm việc dùng chung giữa **Antigravity (Google)** và **Codex (OpenAI)**. Mọi tài liệu kế hoạch, quyết định thiết kế và theo dõi tiến độ đều nằm ở đây để cả hai agent có thể đọc ngữ cảnh và tiếp tục công việc liền mạch.

## Cấu trúc thư mục

```
.ai/
├── CONVENTIONS.md      ← File này — quy ước chung
├── plan.md             ← Kế hoạch triển khai tổng thể
├── tasks.md            ← Danh sách task và tiến độ
├── decisions.md        ← Nhật ký quyết định thiết kế
├── handoff.md          ← Giao tiếp bất đồng bộ giữa hai agent
└── notes/              ← Ghi chú chi tiết cho từng tính năng/component
```

## Quy tắc cho AI agent

### 1. Đọc trước khi làm

Trước khi bắt đầu bất kỳ task nào, agent **phải đọc** các file sau theo thứ tự:

1. `CONVENTIONS.md` — hiểu quy ước
2. `plan.md` — hiểu kế hoạch tổng thể hiện tại
3. `tasks.md` — xem task nào đang mở, đang làm, đã xong
4. `handoff.md` — xem agent kia có nhắn gì không
5. `decisions.md` — xem các quyết định đã thống nhất

### 2. Cập nhật khi làm xong

Sau khi hoàn thành công việc, agent **phải cập nhật**:

- `tasks.md` — đánh dấu task đã xong `[x]` hoặc đang làm `[/]`
- `handoff.md` — ghi lại những gì đã làm, vấn đề gặp phải, và đề xuất cho agent kế tiếp
- `decisions.md` — nếu có quyết định thiết kế mới

### 3. Định dạng thống nhất

- **Ngôn ngữ tài liệu**: Tiếng Việt cho mô tả, tiếng Anh cho tên kỹ thuật
- **Ngày giờ**: ISO 8601 với timezone Việt Nam (`+07:00`)
- **Agent ID**: Dùng `[AGY]` cho Antigravity, `[CDX]` cho Codex
- **Task status**:
  - `[ ]` — chưa bắt đầu
  - `[/]` — đang thực hiện
  - `[x]` — hoàn thành
  - `[!]` — bị chặn / cần review
- **Heading cho entry mới**: Dùng format `### [AGENT_ID] YYYY-MM-DD HH:mm — Tiêu đề`

### 4. Xung đột và phối hợp

- Không sửa đồng thời cùng một file code nếu task chưa được phân chia rõ
- Khi cần agent kia review hoặc tiếp tục, ghi vào `handoff.md` với tag `@AGY` hoặc `@CDX`
- Nếu không đồng ý với quyết định trước đó, ghi phản hồi vào `decisions.md` thay vì tự ý thay đổi

### 5. Tham chiếu code

- Dùng đường dẫn tương đối từ root repo (ví dụ: `filehandler/src/FileHandler.Api/...`)
- Tham chiếu dòng cụ thể khi cần: `file.cs#L10-L20`
- Ghi tên class/method khi đề cập logic

## Lưu ý quan trọng

- Thư mục `.ai/` được commit vào Git để cả hai agent luôn đồng bộ
- File trong `.ai/notes/` dùng cho ghi chú chi tiết từng feature — đặt tên theo pattern: `<feature-name>.md`
- Các quy tắc viết code (comment, format) vẫn tuân theo `AGENTS.md` ở root repo
