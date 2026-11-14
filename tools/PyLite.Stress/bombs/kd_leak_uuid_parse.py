# deadline: 3000
# allocmb: 1024
import uuid
h = 'urn:' * 4000000
while True:
    try:
        uuid.UUID(h)
    except ValueError:
        pass
