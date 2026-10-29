# deadline: 20000
# allocmb: 1024
import io
import zipfile
print(zipfile.is_zipfile(io.BytesIO(b'x' * 8000000)))
try:
    zipfile.ZipFile(io.BytesIO(b'PK' * 4000000))
except zipfile.BadZipFile as e:
    print('clean BadZipFile')
