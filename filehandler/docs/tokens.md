# Quy tắc token r/k/b

Contract này thay thế metadata token trong kế hoạch Translator ban đầu. Filehandler không trả `metadata.tokenMetadata`; Translator không nhận `token_metadata`. Chỉ chuyển `texts` theo đúng thứ tự giữa import, dịch và export.

| Token | Ý nghĩa | Quyền di chuyển |
|---|---|---|
| `<ox:r0>text</ox:r0>` | Văn bản được dịch, giữ liên kết với định dạng nguồn | Trong cùng vùng giữa các boundary |
| `<ox:k0/>` | Nội dung được bảo vệ: inline code, field đơn, ảnh inline khi cấu trúc cho phép | Trong cùng vùng giữa các boundary; giữ nguyên token |
| `<ox:b0/>` | Boundary hoặc nội dung cố định: tab, ngắt dòng, field phức tạp, marker, ranh giới container | Không đổi thứ tự; không token nào được vượt qua |

ID gồm tiền tố `r`, `k`, `b` và số nguyên không âm dạng chuẩn, không có số 0 thừa ở đầu. Không yêu cầu các ID liên tiếp. Mỗi ID nguồn phải xuất hiện đúng một lần trong cùng unit kết quả; không đổi loại token. Run không lồng nhau; ngoài token không có literal text. Trong run, escape `\` thành `\\`, `<` thành `\<`. Run riêng lẻ có thể rỗng nhưng không được xóa hết phần chữ của unit.

```text
Nguồn:   <ox:r0>Version </ox:r0><ox:k0/><ox:r1> required</ox:r1>
Hợp lệ:  <ox:r1>Cần </ox:r1><ox:r0>phiên bản </ox:r0><ox:k0/>

Nguồn:   <ox:r0>Name:</ox:r0><ox:b0/><ox:r1>John</ox:r1>
Sai:     <ox:r1>John</ox:r1><ox:b0/><ox:r0>Tên:</ox:r0>
```

Filehandler chèn boundary tổng hợp tại các lần chuyển container, ví dụ đi vào/ra hyperlink. Boundary tổng hợp không có nội dung để chèn vào file xuất. Những boundary đại diện cho đối tượng thật sẽ khôi phục đối tượng nguồn. Scope và địa chỉ XML vẫn là chi tiết nội bộ để dựng token và render, không được gửi cho model.

Filehandler rút gọn boundary trước khi trả unit: ẩn toàn bộ `bN` ở đầu/cuối, và chỉ giữ boundary đầu tiên trong mỗi nhóm `bN` liên tiếp ở giữa. Bookmark, ngắt trang, field phức tạp, HTML và các đối tượng nguồn tương ứng vẫn được giữ nội bộ để export. `kN` không bị ẩn; các run vẫn không được vượt qua boundary còn hiển thị. ID có thể có khoảng trống sau khi rút gọn.

Nếu chỉ còn một run và không có anchor di chuyển, unit trả plain text (trừ literal chứa prefix dành riêng). Ví dụ `<ox:b0/><ox:r0>改訂履歴</ox:r0><ox:b1/>` trở thành `改訂履歴`. Không tự bỏ token ở client; luôn dùng kết quả import mới.

Riêng Word, các run liền nhau chỉ khác font hint được gộp khi font ASCII, High ANSI và East Asian hiệu lực xác định được là cùng một tên font. Việc kiểm tra xét defaults, chuỗi paragraph/character style và định dạng trực tiếp; theme chưa resolve, style thiếu/vòng lặp hoặc font khác nhau sẽ giữ run riêng. Trong bảng, phải có đủ khai báo từ style/direct formatting để không phụ thuộc table style chưa resolve. XML định dạng gốc vẫn được giữ khi export.

Text không chứa prefix `<ox:` hoặc `</ox:` được xem là plain. Nếu tài liệu chứa các prefix đó theo nghĩa đen, Filehandler bọc nội dung bằng run và escape, ví dụ:

```text
Tài liệu: Use <ox:b0/> literally.
Import:   <ox:r0>Use \<ox:b0/> literally.</ox:r0>
```

TXT cũng áp dụng cách bọc này khi cần. Client gửi nguyên chuỗi đã import và nguyên chuỗi model trả về, không tự bỏ token hay unescape. Filehandler export giải mã và phục hồi literal nguồn.

Translator kiểm tra cú pháp, đủ ID, thứ tự boundary và vùng của từng token trước khi lưu kết quả. Lỗi `token_syntax`, `token_identity`, `token_region`, `empty_translation` khiến đoạn bị retry tối đa ba attempt, sau đó fallback nguồn. Filehandler kiểm tra lại khi export và còn áp dụng ràng buộc riêng của từng định dạng; đọc cả `errors` và `metadata.skipped`.

Markdown khôi phục cấu trúc nguồn bằng đối tượng nội bộ. HTML trong tài liệu người dùng được bảo toàn như nội dung nguồn.

Đây là thay đổi contract không tương thích với các bản import cũ. Dừng các service, cập nhật và khởi động lại, reload client rồi import lại file. Migration SQLite v2 giữ dữ liệu cũ nhưng không cho resume phiên dùng contract cũ. Không ghép Filehandler mới với Translator cũ hoặc ngược lại. [`token-fixtures.json`](../tests/FileHandler.Tests/Fixtures/token-fixtures.json) là bộ mẫu chung được kiểm tra bằng cả C# và Python.
