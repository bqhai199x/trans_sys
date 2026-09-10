# Markdown compatibility profile

Profile: `filehandler-markdown-v1`. Runtime dùng Markdig `1.3.1`, CommonMark mặc định cộng PipeTables, TaskLists, EmphasisExtras, Mathematics và YAML front matter. Phiên bản tài liệu/fixture: `1`.

Mốc đối chiếu Code OSS: repository `microsoft/vscode`, commit `385651c938df8a906869babee516bffd0ddb9829` (tag `1.104.3`, tháng 9/2025), phạm vi `extensions/markdown-language-features`, `src/markdownEngine.ts`, package và test tích hợp. Ở commit này extension khóa `markdown-it ^12.3.2`, bật raw HTML, front matter, source-map; mặc định `breaks=false`, `linkify=true`, `typographer=false` và tắt fuzzy link. Đây là mốc ổn định dùng cho profile; không bao gồm extension Marketplace. VS Code dùng markdown-it còn API dùng Markdig, vì vậy tài liệu này không tuyên bố parity hoàn toàn. Markdig `1.3.1` tương ứng tag commit `cadc2484b96ebb23f21cf382d564a37b526faebc`.

| Cú pháp | VS Code / markdown-it | FileHandler V1 | Fixture/test | Trạng thái |
| --- | --- | --- | --- | --- |
| ATX, paragraph, Unicode | CommonMark | Dịch theo block | acceptance, identity | Đạt |
| Emphasis/strong/strike | Inline nesting | Marker formatting lồng nhau | acceptance | Đạt cơ bản |
| Link trực tiếp | Nhãn + destination | Nhãn dịch, destination giữ | acceptance | Đạt cơ bản |
| Inline code/autolink | Render bảo vệ | Marker rỗng, giữ raw | acceptance, identity | Đạt |
| Fence/code block/mermaid | Không phải prose | Không tạo unit | parser behavior | Đạt |
| List/blockquote | CommonMark | Dịch leaf paragraph, prefix ngoài patch | parser behavior | Đạt cơ bản |
| Pipe table/task list | markdown-it mặc định/GFM | Extension Markdig khóa profile | `profile.md`, identity | Đạt cơ bản |
| Reference link/image title | markdown-it | Parser nhận; mapping phụ thuộc chưa tách unit phụ | chưa đủ fixture | Một phần |
| Math | Chỉ khi built-in Markdown Math đóng góp plugin KaTeX | Markdig Mathematics, inline bảo vệ | `profile.md`, identity | Một phần |
| HTML | markdown-it cho raw HTML | HTML inline bảo vệ toàn bộ | identity | Khác biệt có chủ ý |
| Heading anchor nội bộ | Slug theo VS Code | Chưa remap anchor khi heading đổi | chưa triển khai | Chưa hỗ trợ an toàn |

Nguồn tham chiếu: VS Code `microsoft/vscode`, Markdig `xoofx/markdig` 1.3.1, CommonMark 0.31.2. Không copy mã hoặc fixture bên thứ ba vào repository.
