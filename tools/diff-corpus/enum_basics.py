from enum import Enum, IntEnum, unique
class Season(Enum):
    SPRING = 1
    SUMMER = 2
    AUTUMN = 3
    WINTER = 4
    def describe(self):
        return self.name.lower() + ':' + str(self.value)
class Shape(Enum):
    SQUARE = 2
    DIAMOND = 1
    ALIAS = 2
class Code(IntEnum):
    OK = 200
    MISSING = 404
errors = []
try:
    Season(9)
except ValueError as e:
    errors.append(str(e))
try:
    Season['FALL']
except KeyError as e:
    errors.append(type(e).__name__)
try:
    @unique
    class Dup(Enum):
        A = 1
        B = 1
except ValueError as e:
    errors.append(str(e))
by_season = {Season.SPRING: 'warm', Season.WINTER: 'cold'}
result = [repr(Season.WINTER), str(Season.WINTER), Season.SPRING.name, Season.SPRING.value,
          [s.name for s in Season], len(Season), Season(2) is Season.SUMMER, Season['AUTUMN'] is Season.AUTUMN,
          Season.SUMMER in Season, Season.SPRING == Season.SPRING, Season.SPRING != 1,
          Shape.ALIAS is Shape.SQUARE, Shape.ALIAS.name, [s.name for s in Shape], sorted(Shape.__members__),
          repr(Code.OK), Code.OK == 200, Code.MISSING > Code.OK, Code.OK + 1, Code(404) is Code.MISSING,
          isinstance(Code.OK, int), isinstance(Season.SPRING, Enum), type(Season.SPRING) is Season,
          by_season[Season.WINTER], Season.AUTUMN.describe(), sorted(Code)[0] is Code.OK, errors]
