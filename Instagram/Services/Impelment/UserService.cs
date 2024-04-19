using Instagram.Context;
using Instagram.Entities;
using Instagram.Enumerable;
using Instagram.Payload.Converters.FollowConvert;
using Instagram.Payload.Converters.UserConvert;
using Instagram.Payload.DataRequests;
using Instagram.Payload.DataResponses.Follow;
using Instagram.Payload.DataResponses.User;
using Instagram.Payload.Responses;
using Instagram.Services.Interface;
using Instagram.Validates;
using Microsoft.IdentityModel.Tokens;
using MimeKit;
using MailKit.Security;
using MailKit.Net.Smtp;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using BCryptNet = BCrypt.Net.BCrypt;
using Instagram.Handles;
using System.ComponentModel.DataAnnotations;
using System.Net.Mail;
using System.Net;

namespace Instagram.Services.Impelment
{
    public class UserService : IUserService
    {
        #region private
        private readonly AppDbContext _Context;
        private readonly UserConverter _userConverter;
        private readonly FollowConverter _FlConverter;
        private readonly IConfiguration _configuration;
        private readonly IHttpContextAccessor _contextAccessor;
        private readonly IWebHostEnvironment _webHostEnvironment;
        #endregion
        #region Khaibáo
        public UserService(AppDbContext context, 
                            UserConverter userConverter, 
                            FollowConverter flConverter,
                            IConfiguration configuration, 
                            IHttpContextAccessor contextAccessor, 
                            IWebHostEnvironment webHostEnvironment)
        {
            _Context = context;
            _userConverter = userConverter;
            _FlConverter = flConverter;
            _configuration = configuration;
            _contextAccessor = contextAccessor;
            _webHostEnvironment = webHostEnvironment;
        }
        #endregion
        #region Func
        //Tạo refeshToken ngẫu nhiên
        private string GenerateRefeshToken()
        {
            var random = new byte[32];
            using (var item = RandomNumberGenerator.Create())
            {
                item.GetBytes(random);
                return Convert.ToBase64String(random);
            }
        }
        //Tạo AccessToken
        public ResponseToken GenerateAccessToken(User user)
        {
            var jwtTokenHandler = new JwtSecurityTokenHandler();
            var secretKeyBytes = Encoding.UTF8.GetBytes(_configuration.GetSection("AppSettings:SecretKey").Value);
            var role = _Context.Roles.FirstOrDefault(x=>x.Id == user.RoleId);
            var tokenDesciption = new SecurityTokenDescriptor
            {
                Subject = new ClaimsIdentity(new[]
                {
                    new Claim("ID",user.Id.ToString()),
                    new Claim("Email",user.Email),
                    new Claim(ClaimTypes.Role,role.Code),
                }),
                Expires = DateTime.Now.AddHours(4),
                SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(secretKeyBytes), SecurityAlgorithms.HmacSha256Signature)
            };
            var token = jwtTokenHandler.CreateToken(tokenDesciption);
            var accessToken = jwtTokenHandler.WriteToken(token);
            var refeshToken = GenerateRefeshToken();
            RefeshToken rf = new RefeshToken
            {
                Token = refeshToken,
                ExpiredTime = DateTime.Now.AddDays(1),
                UserId = user.Id,
            };
            _Context.RefreshTokens.Add(rf);
            _Context.SaveChanges();
            ResponseToken result = new ResponseToken
            {
                AccessToken = accessToken,
                RefeshToken = refeshToken
            };
            return result;
        }
        //Đăng nhập
        public ResponseObject<ResponseToken> Login(RequestLogin request)
        {
            if (string.IsNullOrEmpty(request.UserName)||string.IsNullOrEmpty(request.Password))
            {
                return new ResponseObject<ResponseToken>
                {
                    Status = StatusCodes.Status400BadRequest,
                    Message = "Vui lòng điền đầy đủ thông tin!",
                    Data = null
                };
            }
            var user = _Context.Users.FirstOrDefault(x => x.Username.Equals(request.UserName));
            if (user == null)
            {
                return new ResponseObject<ResponseToken>
                {
                    Status = StatusCodes.Status404NotFound,
                    Message = "Người dùng không tồn tại!",
                    Data = null
                };
            }
            bool checkPass = BCryptNet.Verify(request.Password, user.Password);
            if(!checkPass)
            {
                return new ResponseObject<ResponseToken>
                {
                    Status = StatusCodes.Status404NotFound,
                    Message = "Mật khẩu không chính xác!",
                    Data = null
                };
            }
            if(!user.IsActive)
            {
                return new ResponseObject<ResponseToken>
                {
                    Status = StatusCodes.Status200OK,
                    Message = "Tài khoản của bạn đã bị ngừng hoạt động!",
                    Data = null
                };
            }
            if (user.IsLocked)
            {
                return new ResponseObject<ResponseToken>
                {
                    Status = StatusCodes.Status200OK,
                    Message = "Tài khoản của bạn đã bị quản trị viên Ban do vi phạm chính sách!",
                    Data = GenerateAccessToken(user)
                };
            }
            return new ResponseObject<ResponseToken>
            {
                Status = StatusCodes.Status200OK,
                Message = "Đăng nhập thành công!",
                Data = GenerateAccessToken(user)
            };
        }
        //Làm mới Accesstoken
        public ResponseToken RenewAccessToken(Request_RenewAccessToken request)
        {
            throw new NotImplementedException();
        }
        //uploadfile
        private async Task<string> UploadImageAsync(IFormFile imageFile)
        {
            var uploadPath = Path.Combine(_webHostEnvironment.ContentRootPath, "UploadFiles", "Avatars");
            if (!Directory.Exists(uploadPath))
            {
                Directory.CreateDirectory(uploadPath);
            }

            var imageName = Guid.NewGuid().ToString() + Path.GetExtension(imageFile.FileName);
            var imagePath = Path.Combine(uploadPath, imageName);

            using (var stream = new FileStream(imagePath, FileMode.Create))
            {
                imageFile.CopyToAsync(stream);
            }

            return imageName;
        }      
        //Hàm tự sinh mã code
        private string GenerateConfirmCode()
        {
            Random random = new Random();
            int randomNumber = random.Next(1000000);//sinh ngẫu nhiên số từ 1-999999 (6 số)
            string formattedNumber = randomNumber.ToString("D6");// Chuyển số nguyên thành chuỗi có độ dài 6 và thêm số 0 ở đầu nếu cần 
            return formattedNumber;
        }
        //gửi email
        public string SendEmailItem(EmailTo emailTo, string confirmCode)
        {
            var smtpClient = new System.Net.Mail.SmtpClient("smtp.gmail.com")
            {
                Port = 587,
                Credentials = new NetworkCredential("minhquantb00@gmail.com", "jvztzxbtyugsiaea"),
                EnableSsl = true
            };
            try
            {
                var message = new MailMessage();
                message.From = new MailAddress("minhquantb00@gmail.com");
                message.To.Add(emailTo.Email);
                message.Subject = emailTo.Subject;
                message.Body = emailTo.Body + confirmCode;
                message.IsBodyHtml = true;
                smtpClient.Send(message);

                return "Xác nhận gửi email thành công, lấy mã để xác thực";
            }
            catch (Exception ex)
            {
                return "Lỗi khi gửi email: " + ex.Message;
            }
        }
        // Đăng ký
        public async Task<ResponseObject<ResponseUser>> Register(RequestRegister request)
        {
            if (string.IsNullOrEmpty(request.Username)
                || string.IsNullOrEmpty(request.FullName)
                || string.IsNullOrEmpty(request.Email)
                || string.IsNullOrEmpty(request.Password)
                || request.Avatar == null || request.Avatar.Length == 0)
            {
                return new ResponseObject<ResponseUser>
                {
                    Status = StatusCodes.Status400BadRequest,
                    Message = "Vui lòng điền đẩy đủ thông tin!",
                    Data = null
                };
            }

            if (_Context.Users.Any(x => x.Username.ToLower().Equals(request.Username.ToLower())))
            {
                return new ResponseObject<ResponseUser>
                {
                    Status = StatusCodes.Status400BadRequest,
                    Message = "Tài khoản đã tồn tại!",
                    Data = null
                };
            }

            if (_Context.Users.Any(x => x.Email.ToLower().Equals(request.Email.ToLower())))
            {
                return new ResponseObject<ResponseUser>
                {
                    Status = StatusCodes.Status400BadRequest,
                    Message = "Email đã tồn tại!",
                    Data = null
                };
            }

            if (!ValidateEmail.IsEmail(request.Email))
            {
                return new ResponseObject<ResponseUser>
                {
                    Status = StatusCodes.Status400BadRequest,
                    Message = "Email không đúng định dạng!",
                    Data = null
                };
            }

            using (var transaction = _Context.Database.BeginTransaction())
            {
                try
                {
                    var user = new User
                    {
                        Username = request.Username,
                        FullName = request.FullName,
                        Email = request.Email,
                        Password = BCryptNet.HashPassword(request.Password),
                        RoleId = 1,
                        Avatar = await UploadImageAsync(request.Avatar),
                        DateOfBirth = request.DateOfBirth,
                        UserStatusId = 2,
                        IsActive = true,
                        IsLocked = false
                    };

                    _Context.Users.Add(user);
                    _Context.SaveChanges();

                    var confirmCode = GenerateConfirmCode();

                    var confirmEmail = new ConfirmEmail
                    {
                        UserId = user.Id,
                        ExpiredTime = DateTime.UtcNow.AddHours(3),
                        ConfirmCode = confirmCode,
                        Confirmed = false
                    };

                    _Context.ConfirmEmails.Add(confirmEmail);
                    _Context.SaveChanges();

                    SendEmailItem(new EmailTo
                    {
                        Email = user.Email,
                        Body = $"Ma xac nhan cua ban la: ",
                        Subject = "Nhan ma xac nhan tai day: "
                    }, confirmEmail.ConfirmCode);

                    transaction.Commit();

                    return new ResponseObject<ResponseUser>
                    {
                        Status = StatusCodes.Status200OK,
                        Message = "Đăng ký thành công! Vui lòng kiểm tra email để xác nhận địa chỉ email.",
                        Data = _userConverter.EntityToDTO(user)
                    };
                }
                catch (Exception ex)
                {
                    transaction.Rollback();

                    return new ResponseObject<ResponseUser>
                    {
                        Status = StatusCodes.Status500InternalServerError,
                        Message = "Đã xảy ra lỗi trong quá trình đăng ký.",
                        Data = null
                    };
                }
            }
        }
        //Xác nhận mã code
        public async Task<ResponseObject<string>> ConfirmEmail(string ConfirmCode)
        {
            var currentUser = _contextAccessor.HttpContext.User;
            if (!currentUser.Identity.IsAuthenticated)
            {
                return new ResponseObject<string>
                {
                    Status = StatusCodes.Status400BadRequest,
                    Message = "Người dùng chưa được xác thực!",
                    Data = "Vui lòng đăng nhập lại"
                };
            }
            var claim = currentUser.FindFirst("ID");
            var IdUser = int.Parse(claim.Value);
            var confirmEmail = _Context.ConfirmEmails.FirstOrDefault(x => x.UserId == IdUser);
            if (confirmEmail.ConfirmCode != ConfirmCode)
            {
                return new ResponseObject<string>
                {
                    Status = StatusCodes.Status400BadRequest,
                    Message = "Mã code không hợp lệ!",
                    Data = "Vui lòng nhập lại"
                };
            }
            // Kiểm tra xem mã code đã hết hạn hay chưa
            if (DateTime.UtcNow > confirmEmail.ExpiredTime)
            {
                return new ResponseObject<string>
                {
                    Status = StatusCodes.Status400BadRequest,
                    Message = "Mã code đã hết hạn!",
                    Data = "Vui lòng gửi lại mã xác nhận"
                };
            }
            // Đặt trạng thái xác nhận của email là đã xác nhận
            confirmEmail.Confirmed = true;
            _Context.SaveChanges();
            return new ResponseObject<string>
            {
                Status = StatusCodes.Status200OK,
                Message = "Xác nhận email thành công!",
                Data = "Giờ đây bạn đã có thể sử dụng tài khoản của mình"
            };
        }
        //gửi lại mã nếu code hết hạn
        public async Task<ResponseObject<string>> ResendCode() 
        {
            var currentUser = _contextAccessor.HttpContext.User;
            if (!currentUser.Identity.IsAuthenticated)
            {
                return new ResponseObject<string>
                {
                    Status = StatusCodes.Status400BadRequest,
                    Message = "Người dùng chưa được xác thực!",
                    Data = "Vui lòng đăng nhập lại"
                };
            }
            var claim = currentUser.FindFirst("ID");
            var IdUser = int.Parse(claim.Value);
            var user = _Context.Users.FirstOrDefault(x => x.Id == IdUser);
            // Tạo mã xác nhận mới
            var newConfirmCode = GenerateConfirmCode();
            // Cập nhật mã xác nhận mới vào bảng ConfirmEmail (bạn cần viết hàm cập nhật trong trường hợp này)
            var confirmEmail = _Context.ConfirmEmails.FirstOrDefault(x => x.UserId == IdUser);
            confirmEmail.ConfirmCode = newConfirmCode;
            // Gửi lại mã xác nhận mới đến địa chỉ email đã đăng ký
            SendEmailItem(new EmailTo
            {
                Email = user.Email,
                Body = $"Ma xac nhan cua ban la: ",
                Subject = "Nhan ma xac nhan tai day: "
            }, confirmEmail.ConfirmCode);
            return new ResponseObject<string>
            {
                Status = StatusCodes.Status200OK,
                Message = "Gửi lại mã xác nhận email thành công!",
                Data = "Hãy vào hòm thư để lấy mã xác nhận email của bạn"
            };
        }
        //Get All (Chỉ Admin)
        public ResponseObject<IQueryable<ResponseUser>> GetAll()
        {
            var currentUser = _contextAccessor.HttpContext.User;
            if (!currentUser.Identity.IsAuthenticated)
            {
                return new ResponseObject<IQueryable<ResponseUser>>
                {
                    Status = StatusCodes.Status400BadRequest,
                    Message = "Người dùng chưa được xác thực!",
                    Data = null
                };
            }
            if (currentUser.IsInRole("Admin"))
            {
                var userList = _Context.Users.ToList().Select(x => _userConverter.EntityToDTO(x)).AsQueryable();
                if (!userList.Any())
                {
                    return new ResponseObject<IQueryable<ResponseUser>>
                    {
                        Status = StatusCodes.Status404NotFound,
                        Message = "Danh sách trống!",
                        Data = null
                    };
                }
                return new ResponseObject<IQueryable<ResponseUser>>
                {
                    Status = StatusCodes.Status200OK,
                    Message = "Thực hiện thao tác thành công!",
                    Data = userList
                };
            }
            return new ResponseObject<IQueryable<ResponseUser>>
            {
                Status = StatusCodes.Status401Unauthorized,
                Message = "Bạn không đủ quyền hạn để thực hiện thao tác này!",
                Data = null
            };
        }
        //Sửa thông tin người dùng (Không sửa Role)
        public async Task<ResponseObject<ResponseUser>> UpdateUserForAdmin(int id, RequestUser Request)
        {
            var currentUser = _contextAccessor.HttpContext.User;
            if (!currentUser.Identity.IsAuthenticated)
            {
                return new ResponseObject<ResponseUser>
                {
                    Status = StatusCodes.Status400BadRequest,
                    Message = "Người dùng chưa được xác thực!",
                    Data = null
                };
            }
            if (currentUser.IsInRole("admin"))
            {
                var user = _Context.Users.SingleOrDefault(x => x.Id == id);
                if (user is null)
                {
                    return new ResponseObject<ResponseUser>
                    {
                        Status = StatusCodes.Status404NotFound,
                        Message = "Không tìm thấy người dùng này!",
                        Data = null
                    };
                }
                if (string.IsNullOrEmpty(Request.Username)
                    || string.IsNullOrEmpty(Request.Password)
                    || string.IsNullOrEmpty(Request.FullName)
                    || string.IsNullOrEmpty(Request.Email)
                    || Request.Avatar == null || Request.Avatar.Length == 0)
                {
                    return new ResponseObject<ResponseUser>
                    {
                        Status = StatusCodes.Status400BadRequest,
                        Message = "Vui lòng điền đầy đủ thông tin!",
                        Data = null
                    };
                }
                if (_Context.Users.Any(x => !Request.Username.ToLower().Equals(user.Username.ToLower()) && x.Username.ToLower().Equals(Request.Username.ToLower())))
                {
                    return new ResponseObject<ResponseUser>
                    {
                        Status = StatusCodes.Status400BadRequest,
                        Message = "Tài khoản đã tồn tại!",
                        Data = null
                    };
                }
                if (_Context.Users.Any(x => x.Email.ToLower().Equals(Request.Email.ToLower()) && !Request.Email.ToLower().Equals(user.Email.ToLower())))
                {
                    return new ResponseObject<ResponseUser>
                    {
                        Status = StatusCodes.Status400BadRequest,
                        Message = "Email đã tồn tại!",
                        Data = null
                    };
                }
                if (!ValidateEmail.IsEmail(Request.Email))
                {
                    return new ResponseObject<ResponseUser>
                    {
                        Status = StatusCodes.Status400BadRequest,
                        Message = "Eamil không đúng định dạng!",
                        Data = null
                    };
                }
                user.Username = Request.Username;
                user.Email = Request.Email;
                user.Password = BCryptNet.HashPassword(Request.Password);
                var avatarName = await UploadImageAsync(Request.Avatar);
                user.Avatar = avatarName;
                user.FullName = Request.FullName;
                user.IsActive = Request.IsActive;
                user.IsLocked = Request.IsLocked;
                user.DateOfBirth = Request.DateOfBirth;
                user.UserStatusId = Request.StatusUserId;
                _Context.SaveChanges();
                return new ResponseObject<ResponseUser>
                {
                    Status = StatusCodes.Status200OK,
                    Message = "Cập nhật người dùng thành công!",
                    Data = _userConverter.EntityToDTO(user)
                };
            }
            return new ResponseObject<ResponseUser>
            {
                Status = StatusCodes.Status200OK,
                Message = "Bạn không đủ quyền hạn để sử dụng chức năng này!",
                Data = null
            };
        }
        //sửa thông tin của chính người dùng đang đăng nhập
        public async Task<ResponseObject<ResponseUser>> UpdateUserForUserLogin(RequestUser Request)
        {
            var currentUser = _contextAccessor.HttpContext.User;
            if (!currentUser.Identity.IsAuthenticated)
            {
                return new ResponseObject<ResponseUser>
                {
                    Status = StatusCodes.Status400BadRequest,
                    Message = "Người dùng chưa được xác thực!",
                    Data = null
                };
            }
            var claim = currentUser.FindFirst("ID");
            var IdUser = int.Parse(claim.Value);
            var user = _Context.Users.SingleOrDefault(x => x.Id == IdUser);
            var confirmEmail = _Context.ConfirmEmails.FirstOrDefault(x => x.UserId == IdUser);
            if(!confirmEmail.Confirmed || confirmEmail == null)
            {
                return new ResponseObject<ResponseUser>
                {
                    Status = StatusCodes.Status200OK,
                    Message = "Bạn chưa xác nhận email!",
                    Data = null
                };
            }
            if (!user.IsActive)
            {
                return new ResponseObject<ResponseUser>
                {
                    Status = StatusCodes.Status200OK,
                    Message = "Tài khoản của bạn đã ngừng hoạt động!",
                    Data = null
                };
            }
            if (user.IsLocked)
            {
                return new ResponseObject<ResponseUser>
                {
                    Status = StatusCodes.Status200OK,
                    Message = "Tài khoản của bạn đã quản trị viên Ban do vi phạm chính sách!",
                    Data = null
                };
            }
            if (string.IsNullOrEmpty(Request.Username)
                || string.IsNullOrEmpty(Request.Password)
                || string.IsNullOrEmpty(Request.FullName)
                || string.IsNullOrEmpty(Request.Email)
                || Request.Avatar == null || Request.Avatar.Length == 0)
            {
                return new ResponseObject<ResponseUser>
                {
                    Status = StatusCodes.Status400BadRequest,
                    Message = "Vui lòng điền đầy đủ thông tin!",
                    Data = null
                };
            }
            if (_Context.Users.Any(x => !Request.Username.ToLower().Equals(user.Username.ToLower()) && x.Username.ToLower().Equals(Request.Username.ToLower())))
            {
                return new ResponseObject<ResponseUser>
                {
                    Status = StatusCodes.Status400BadRequest,
                    Message = "Tài khoản đã tồn tại!",
                    Data = null
                };
            }
            if (_Context.Users.Any(x => x.Email.ToLower().Equals(Request.Email.ToLower()) && !Request.Email.ToLower().Equals(user.Email.ToLower())))
            {
                return new ResponseObject<ResponseUser>
                {
                    Status = StatusCodes.Status400BadRequest,
                    Message = "Email đã tồn tại!",
                    Data = null
                };
            }
            if (!ValidateEmail.IsEmail(Request.Email))
            {
                return new ResponseObject<ResponseUser>
                {
                    Status = StatusCodes.Status400BadRequest,
                    Message = "Eamil không đúng định dạng!",
                    Data = null
                };
            }
            user.Username = Request.Username;
            user.Email = Request.Email;
            user.Password = BCryptNet.HashPassword(Request.Password);
            var avatarName = await UploadImageAsync(Request.Avatar);
            user.Avatar = avatarName;
            user.FullName = Request.FullName;
            user.IsActive = Request.IsActive;
            user.IsLocked = Request.IsLocked;
            user.DateOfBirth = Request.DateOfBirth;
            user.UserStatusId = Request.StatusUserId;
            _Context.SaveChanges();
            return new ResponseObject<ResponseUser>
            {
                Status = StatusCodes.Status200OK,
                Message = "Cập nhật người dùng thành công!",
                Data = _userConverter.EntityToDTO(user)
            };
        }
        //Xóa bài viết và các bảng liên quan khi xóa người dùng
        private void deletePost(int id)
        {
            //Gỡ bài viết
            var posts = _Context.Posts.Where(x => x.UserId == id).ToList();
            for (int i = 0; i < posts.Count(); i++)
            {
                //Xóa likePost
                var LikePost = _Context.UserLikePosts.Where(c => c.PostId == posts[i].Id);
                _Context.UserLikePosts.RemoveRange(LikePost);
                //Xóa toàn bộ Likecoment của coment trong bài viết
                var ComentPost = _Context.userCommentPosts.Where(c => c.PostId == posts[i].Id).ToList();
                for (int j = 0; j < ComentPost.Count(); j++)
                {
                    var likeComent = _Context.UserLikesComments.Where(c => c.UserCommentPostId == ComentPost[j].Id);
                    _Context.UserLikesComments.RemoveRange(likeComent);
                }
                //Xóa toàn bộ coment trong bài viết
                _Context.userCommentPosts.RemoveRange(ComentPost);
            }
            _Context.Posts.RemoveRange(posts);
            _Context.SaveChanges();
        }
        //Xóa vĩnh viễn người dùng (chỉ admin)
        public ResponseObject<IQueryable<ResponseUser>> DeleteUser(int id)
        {
            var currentUser = _contextAccessor.HttpContext.User;
            if (!currentUser.Identity.IsAuthenticated)
            {
                return new ResponseObject<IQueryable<ResponseUser>>
                {
                    Status = StatusCodes.Status400BadRequest,
                    Message = "Người dùng chưa xác thực!",
                    Data = null
                };
            }
            if (currentUser.IsInRole("Admin"))
            {
                var user = _Context.Users.Find(id);
                if (user != null)
                {
                    //Hướng xử lý là xóa Bảng Post trước khi xóa user nhưng vì bảng post sẽ tự bị xóa
                    //khi xóa user nên phải xóa những thứ dính tới post trước(PostCollection, ReportPost,likePost,comentPost)
                    // Bước 1: xóa PostCollection, ReportPost,likePost
                    // Bước 2: Xóa bảng likeComent trước khi xóa bảng comentPost
                    // => xong những thứ dính tới Post
                    // bây giờ Xóa những thứ dính tới user(Likecoment, likePost,comentPost)-(Không cần xóa bảng report vì xóa chung ở trên rồi)
                    // Bước 3: xóa like post
                    // Bước 4 : Xóa likeComent trước khi xóa comentPost
                    // Bước 5 : xóa user (những bảng dính tới user không nhắc đến ở trên sẽ tự động bị xóa)

                    // xóa bảng User like coment
                    var UlikeComent = _Context.UserLikesComments.Where(k=>k.UserId == id);
                    _Context.UserLikesComments.RemoveRange(UlikeComent);
                    //Xóa bảng PostCollection
                    var Collection = _Context.Collections.Where(x => x.UserId == id).ToList();
                    for (int i = 0; i < Collection.Count(); i++)
                    {
                        var postCollection = _Context.PostCollections.Where(x => x.CollectionId == Collection[i].Id);
                        _Context.PostCollections.RemoveRange(postCollection);
                    }
                    //Xóa bảng Report (bài report của user và user bị report)
                    var report = _Context.Reports.Where(x => x.UserReportId == id || x.UserReporterId == id);
                    _Context.Reports.RemoveRange(report);
                    //Xóa những thứ dính tới post và xóa post
                    deletePost(id);
                    //Xóa bảng like coment của các Coment của user
                    var ComentPost = _Context.userCommentPosts.Where(m => m.UserId == id).ToList();
                    for (int j = 0; j < ComentPost.Count(); j++)
                    {
                        var likeComent = _Context.UserLikesComments.Where(c => c.UserCommentPostId == ComentPost[j].Id);
                        _Context.UserLikesComments.RemoveRange(likeComent);
                    }
                    //xóa coment
                    _Context.userCommentPosts.RemoveRange(ComentPost);
                    //xóa bảng RelationShip
                    var follow = _Context.RelationShips.Where(x => x.FollowerId == id || x.FollowingId == id);
                    _Context.RelationShips.RemoveRange(follow);
                    //xóa bảng like post (user like post)
                    var likePost = _Context.UserLikePosts.Where(x => x.UserId == id);
                    _Context.UserLikePosts.RemoveRange(likePost);
                    //xóa người dùng()
                    _Context.Users.Remove(user);
                    _Context.SaveChanges();
                    var listUser = _Context.Users.ToList().Select(x => _userConverter.EntityToDTO(x)).AsQueryable();
                    return new ResponseObject<IQueryable<ResponseUser>>
                    {
                        Status = StatusCodes.Status200OK,
                        Message = "Xóa tài khoản thành công!",
                        Data = listUser
                    };
                }
                return new ResponseObject<IQueryable<ResponseUser>>
                {
                    Status = StatusCodes.Status404NotFound,
                    Message = "Người dùng không tồn tại!",
                    Data = null
                };
            }
            return new ResponseObject<IQueryable<ResponseUser>>
            {
                Status = StatusCodes.Status401Unauthorized,
                Message = "Bạn không đủ quyền hạn để dùng chức năng này!",
                Data = null
            };
        }
        //Ban tài khoản(Admin)
        public ResponseObject<ResponseUser> BanAccount(int iduser)
        {
            var currentUser = _contextAccessor.HttpContext.User;
            if (!currentUser.Identity.IsAuthenticated)
            {
                return new ResponseObject<ResponseUser>
                {
                    Status = StatusCodes.Status400BadRequest,
                    Message = "Người dùng chưa xác thực!",
                    Data = null
                };
            }
            if (currentUser.IsInRole("Admin"))
            {
                var user = _Context.Users.FirstOrDefault(x => x.Id == iduser && x.IsActive);
                if (user == null)
                {
                    return new ResponseObject<ResponseUser>
                    {
                        Status = StatusCodes.Status400BadRequest,
                        Message = "Tài khoản này đã bị ban!",
                        Data = null
                    };
                }
                if (user.IsLocked)
                {
                    user.IsLocked = false;
                    _Context.SaveChanges();
                    return new ResponseObject<ResponseUser>
                    {
                        Status = StatusCodes.Status200OK,
                        Message = "Mở Ban tài khoản thành công!",
                        Data = _userConverter.EntityToDTO(user)
                    };
                }
                else
                {

                    user.IsLocked = true;
                    _Context.SaveChanges();
                    return new ResponseObject<ResponseUser>
                    {
                        Status = StatusCodes.Status200OK,
                        Message = "Ban tài khoản thành công!",
                        Data = _userConverter.EntityToDTO(user)
                    };
                }
            }
            return new ResponseObject<ResponseUser>
            {
                Status = StatusCodes.Status200OK,
                Message = "Bạn không đủ quyền để thực hiện chức năng này!",
                Data = null
            };
        }
        //Tắt hoặc bật hoạt động tài khoản
        public ResponseObject<ResponseUser> ActiveAccount()
        {
            var currentUser = _contextAccessor.HttpContext.User;
            if (!currentUser.Identity.IsAuthenticated)
            {
                return new ResponseObject<ResponseUser>
                {
                    Status = StatusCodes.Status400BadRequest,
                    Message = "Người dùng chưa xác thực!",
                    Data = null
                };
            }
            var claim = currentUser.FindFirst("ID");
            var IdUser = int.Parse(claim.Value);
            var user = _Context.Users.SingleOrDefault(x => x.Id == IdUser);
            var confirmEmail = _Context.ConfirmEmails.FirstOrDefault(x => x.UserId == IdUser);
            if (!confirmEmail.Confirmed || confirmEmail == null)
            {
                return new ResponseObject<ResponseUser>
                {
                    Status = StatusCodes.Status200OK,
                    Message = "Bạn chưa xác nhận email!",
                    Data = null
                };
            }
            if (user.IsLocked)
            {
                return new ResponseObject<ResponseUser>
                {
                    Status = StatusCodes.Status400BadRequest,
                    Message = "Tài khoản của bạn đã bị quản trị viên ban do vi phạm chính sách!",
                    Data = null
                };
            }
            if(user.IsActive)
            {
                user.IsActive = false;
                _Context.SaveChanges();
                return new ResponseObject<ResponseUser>
                {
                    Status = StatusCodes.Status200OK,
                    Message = "Tài khoản của bạn đã hoạt đông trở lại!",
                    Data = _userConverter.EntityToDTO(user)
                };
            }
            else
            {
                user.IsActive = true;
                _Context.SaveChanges();
                return new ResponseObject<ResponseUser>
                {
                    Status = StatusCodes.Status200OK,
                    Message = "Tắt hoạt động tài khoản thành công!",
                    Data = null
                };
            }
        }
        //Set role for user (only Admin)
        public ResponseObject<ResponseUser> SetRoleForUser(int id, RoleType role)
        {
            var currentUser = _contextAccessor.HttpContext.User;
            if (!currentUser.Identity.IsAuthenticated)
            {
                return new ResponseObject<ResponseUser>
                {
                    Status = StatusCodes.Status400BadRequest,
                    Message = "Người dùng chưa xác thực!",
                    Data = null
                };
            }
            if (currentUser.IsInRole("Admin"))
            {
                var user = _Context.Users.SingleOrDefault(x => x.Id == id);
                if (user == null)
                {
                    return new ResponseObject<ResponseUser>
                    {
                        Status = StatusCodes.Status404NotFound,
                        Message = "Người dùng không tồn tại!",
                        Data = null
                    };
                }
                if (!_Context.Roles.Any(x => x.Id == (int)role))
                {
                    return new ResponseObject<ResponseUser>
                    {
                        Status = StatusCodes.Status404NotFound,
                        Message = "Không tìm thấy role này!",
                        Data = null
                    };
                }
                user.RoleId = (int)role;
                _Context.SaveChanges();
                return new ResponseObject<ResponseUser>
                {
                    Status = StatusCodes.Status200OK,
                    Message = "Sửa role thành công!",
                    Data = _userConverter.EntityToDTO(user)
                };
            }
            return new ResponseObject<ResponseUser>
            {
                Status = StatusCodes.Status400BadRequest,
                Message = "Bạn không đủ quyền hạn để sử dụng chức năng này!",
                Data = null
            };
        }
        //User đang login đi follow user khác
        public ResponseObject<ResponseFollow> FollowingUser(int idUserWantFollow)
        {
            var currentUser = _contextAccessor.HttpContext.User;
            if (!currentUser.Identity.IsAuthenticated)
            {
                return new ResponseObject<ResponseFollow>
                {
                    Status = StatusCodes.Status400BadRequest,
                    Message = "Người dùng chưa xác thực",
                    Data = null
                };
            }
            var claim = currentUser.FindFirst("ID");
            var IDUser = int.Parse(claim.Value);
            var user = _Context.Users.FirstOrDefault(x => x.Id == IDUser);
            var confirmEmail = _Context.ConfirmEmails.FirstOrDefault(x => x.UserId == IDUser);
            if (!confirmEmail.Confirmed || confirmEmail == null)
            {
                return new ResponseObject<ResponseFollow>
                {
                    Status = StatusCodes.Status200OK,
                    Message = "Bạn chưa xác nhận email!",
                    Data = null
                };
            }
            if (!user.IsActive)
            {
                return new ResponseObject<ResponseFollow>
                {
                    Status = StatusCodes.Status200OK,
                    Message = "Tài khoản của bạn đã bị ngưng hoạt động!",
                    Data = null
                };
            }
            if (user.IsLocked)
            {//quản trị viên Ban do vi phạm chính sách
                return new ResponseObject<ResponseFollow>
                {
                    Status = StatusCodes.Status200OK,
                    Message = "Tài khoản của bạn đã bị quản trị viên Ban do vi phạm chính sách!",
                    Data = null
                };
            }
            if (IDUser == idUserWantFollow)
            {
                return new ResponseObject<ResponseFollow>
                {
                    Status = StatusCodes.Status400BadRequest,
                    Message = "Không thể folow chính mình!",
                    Data = null
                };
            }
            if (!_Context.Users.Any(x => x.Id == idUserWantFollow))
            {
                return new ResponseObject<ResponseFollow>
                {
                    Status = StatusCodes.Status404NotFound,
                    Message = "Không tìm thấy người dùng này!",
                    Data = null
                };
            }
            Relationship relationship = new Relationship
            {
                FollowerId = IDUser,
                FollowingId = idUserWantFollow
            };
            _Context.RelationShips.Add(relationship);
            _Context.SaveChanges();
            return new ResponseObject<ResponseFollow>
            {
                Status = StatusCodes.Status200OK,
                Message = "Folow thành công!",
                Data = _FlConverter.FollowToDTO(IDUser)
            };
        }
        //Xem số lượng người follow mình(User đang login)
        public ResponseObject<ResponseFollow> GetRelationShipOfUser()
        {
            var currentUser = _contextAccessor.HttpContext.User;
            if(!currentUser.Identity.IsAuthenticated)
            {
                return new ResponseObject<ResponseFollow>
                {
                    Status = StatusCodes.Status400BadRequest,
                    Message = "Người dùng chưa xác thực",
                    Data = null
                };
            }
            var claim = currentUser.FindFirst("ID");
            var IDUser = int.Parse(claim.Value);
            var user = _Context.Users.FirstOrDefault(x => x.Id == IDUser);
            var confirmEmail = _Context.ConfirmEmails.FirstOrDefault(x => x.UserId == IDUser);
            if (!confirmEmail.Confirmed || confirmEmail == null)
            {
                return new ResponseObject<ResponseFollow>
                {
                    Status = StatusCodes.Status200OK,
                    Message = "Bạn chưa xác nhận email!",
                    Data = null
                };
            }
            if (!user.IsActive)
            {
                return new ResponseObject<ResponseFollow>
                {
                    Status = StatusCodes.Status200OK,
                    Message = "Tài khoản của bạn đã bị dừng hoạt động!",
                    Data = null
                };
            }
            if (user.IsLocked)
            {
                return new ResponseObject<ResponseFollow>
                {
                    Status = StatusCodes.Status200OK,
                    Message = "Tài khoản của bạn đã bị quản trị viên Ban do vi phạm chính sách!",
                    Data = null
                };
            }
            return new ResponseObject<ResponseFollow>
            {
                Status = StatusCodes.Status200OK,
                Message = "Thực hiện thao tác thành công",
                Data = _FlConverter.FollowToDTO(IDUser)
            };
        }
        //UnFollow người dùng
        public ResponseObject<ResponseFollow> UnFollow(int idUserWantUnFollow)
        {
            var currentUser = _contextAccessor.HttpContext.User;
            if (!currentUser.Identity.IsAuthenticated)
            {
                return new ResponseObject<ResponseFollow>
                {
                    Status = StatusCodes.Status400BadRequest,
                    Message = "Người dùng chưa xác thực",
                    Data = null
                };
            }
            var claim = currentUser.FindFirst("ID");
            var IDUser = int.Parse(claim.Value);
            var user = _Context.Users.FirstOrDefault(x => x.Id == IDUser);
            var confirmEmail = _Context.ConfirmEmails.FirstOrDefault(x => x.UserId == IDUser);
            if (!confirmEmail.Confirmed || confirmEmail == null)
            {
                return new ResponseObject<ResponseFollow>
                {
                    Status = StatusCodes.Status200OK,
                    Message = "Bạn chưa xác nhận email!",
                    Data = null
                };
            }
            if (!user.IsActive)
            {
                return new ResponseObject<ResponseFollow>
                {
                    Status = StatusCodes.Status200OK,
                    Message = "Tài khoản của bạn đã bị ngừng hoạt động!",
                    Data = null
                };
            }
            if (user.IsLocked)
            {
                return new ResponseObject<ResponseFollow>
                {
                    Status = StatusCodes.Status200OK,
                    Message = "Tài khoản của bạn đã bị quản trị viên Ban do vi phạm chính sách!",
                    Data = null
                };
            }
            if (_Context.Users.Any(y => y.Id == idUserWantUnFollow))
            {
                var relationship = _Context.RelationShips.FirstOrDefault(x=>x.FollowerId == IDUser && x.FollowingId == idUserWantUnFollow);
                if (relationship == null)
                {
                    return new ResponseObject<ResponseFollow>
                    {
                        Status = StatusCodes.Status404NotFound,
                        Message = "Bạn chưa follow người này",
                        Data = null
                    };
                }
                _Context.Remove(relationship);
                _Context.SaveChanges();
                return new ResponseObject<ResponseFollow>
                {
                    Status = StatusCodes.Status200OK,
                    Message = "Thực hiện thao tác thành công",
                    Data = _FlConverter.FollowToDTO(IDUser)
                };
            }
            return new ResponseObject<ResponseFollow>
            {
                Status = StatusCodes.Status404NotFound,
                Message = "Người dùng bạn muốn bỏ theo dõi không tồn tại",
                Data = null
            };
        }
        #endregion
    }
}
